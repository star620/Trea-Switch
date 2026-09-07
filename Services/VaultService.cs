using System.Text.Json;

namespace TraeSwitch.Services;

public sealed record VaultMeta(string Account, string CreatedUtc, List<VaultEntry> Entries);
public sealed record VaultEntry(string Rel, long Len, string Sha256Hex);

/// <summary>账号建档展示信息：建档时间（本地）与载体文件数；未建档为 null。</summary>
public sealed record VaultInfo(DateTime? CreatedLocal, int EntryCount);

/// <summary>
/// 按账号把"载体"原样字节备份到 vaultRoot\&lt;Account&gt;\，支持恢复与哈希校验。
/// Fingerprint 条目可为「单文件」或「目录」：目录条目会把整棵子树作为载体处理，
/// 恢复时先删除 live 目标再整树写回，避免 LevelDB 等滚动日志产生孤儿文件。
/// 只做字节拷贝，不解密。
/// </summary>
public sealed class VaultService(string rootDir, string vaultRoot)
{
    public string AccountDir(string account) => Path.Combine(vaultRoot, Sanitize(account));
    public string MetaPath(string account) => Path.Combine(AccountDir(account), "meta.json");

    /// <summary>展开指纹条目为 root 相对文件路径（目录则递归其中全部文件）。</summary>
    public static IEnumerable<string> ExpandFingerprint(string rootDir, IEnumerable<string> fingerprint)
    {
        foreach (var rel in fingerprint)
        {
            var p = Path.Combine(rootDir, rel);
            if (Directory.Exists(p))
            {
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    yield return Path.GetRelativePath(rootDir, f);
            }
            else if (File.Exists(p))
            {
                yield return rel;
            }
        }
    }

    /// <summary>
    /// 把 srcRoot\rel 覆盖写到 dstRoot\rel。目录条目先整删 dst 目标再复制整树，
    /// 文件条目直接覆盖。任一侧不存在该条目则跳过。
    /// </summary>
    public static void OverwriteEntry(string srcRoot, string dstRoot, string rel)
    {
        var src = Path.Combine(srcRoot, rel);
        var dst = Path.Combine(dstRoot, rel);
        if (Directory.Exists(src))
        {
            if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dst, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, overwrite: true);
            }
        }
        else if (File.Exists(src))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
    }

    public async Task<int> BackupAsync(string account, IEnumerable<string> fingerprint)
    {
        var dir = AccountDir(account);
        Directory.CreateDirectory(dir);
        var entries = new List<VaultEntry>();
        foreach (var rel in fingerprint)
        {
            var src = Path.Combine(rootDir, rel);
            if (Directory.Exists(src))
            {
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var dst = Path.Combine(dir, Path.GetRelativePath(rootDir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(f, dst, overwrite: true);
                    entries.Add(new VaultEntry(Path.GetRelativePath(rootDir, f), new FileInfo(f).Length, CarrierProfiler.HashFile(f)));
                }
            }
            else if (File.Exists(src))
            {
                var dst = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
                entries.Add(new VaultEntry(rel, new FileInfo(src).Length, CarrierProfiler.HashFile(src)));
            }
        }
        var meta = new VaultMeta(account, DateTime.UtcNow.ToString("o"), entries);
        await File.WriteAllTextAsync(MetaPath(account), JsonSerializer.Serialize(meta, JsonOpts));
        return entries.Count;
    }

    /// <summary>读取某账号 meta 的载体条目；无 meta 文件返回 null。</summary>
    public List<VaultEntry>? ReadEntries(string account)
    {
        if (!File.Exists(MetaPath(account))) return null;
        try
        {
            var meta = JsonSerializer.Deserialize<VaultMeta>(File.ReadAllText(MetaPath(account)));
            return meta?.Entries ?? [];
        }
        catch { return null; }
    }

    /// <summary>某账号建档的展示信息；未建档返回 null。</summary>
    public VaultInfo? GetInfo(string account)
    {
        if (!File.Exists(MetaPath(account))) return null;
        try
        {
            var meta = JsonSerializer.Deserialize<VaultMeta>(File.ReadAllText(MetaPath(account)));
            if (meta == null) return null;
            DateTime? created = DateTime.TryParse(meta.CreatedUtc, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                ? t.ToLocalTime() : null;
            return new VaultInfo(created, meta.Entries.Count);
        }
        catch { return null; }
    }

    /// <summary>leveldb 内部管理文件：内容与账号无关、随每次运行/compaction 改写，识别时应排除。</summary>
    private static bool IsLeveldbNoise(string rel)
    {
        var name = Path.GetFileName(rel);
        return name == "LOG" || name == "LOG.old" || name == "CURRENT"
            || name == "LOCK" || name.StartsWith("MANIFEST-", StringComparison.Ordinal);
    }

    /// <summary>
    /// 运行痕迹文件：含时间戳/会话状态，每次客户端运行都会改写，与账号身份无关。
    /// 建档时间不同的两个账号这些文件哈希恰好不同，若计入判别集会稀释区分度。
    /// </summary>
    private static bool IsRuntimeStateFile(string rel)
    {
        var norm = rel.Replace('\\', '/').ToLowerInvariant();
        return norm == "local storage/config.db"
            || norm == "network/network persistent state"
            || norm == "user/globalstorage/storage.json";
    }

    /// <summary>识别评分时不应计入的文件（leveldb 管理噪声 + 运行痕迹）。</summary>
    private static bool IsNonDiscriminative(string rel)
        => IsLeveldbNoise(rel) || IsRuntimeStateFile(rel);

    /// <summary>计算 live 与某账号快照的相似度 0..1：meta 中每条"非噪声"载体文件在 live 原路径
    /// 存在且哈希一致即命中；命中数 / 可比数。live 侧多出的文件不判负。
    /// 客户端运行会改写 LOG/滚动日志/运行痕迹文件，故排除它们后评分才稳定。无档返回 null。
    /// </summary>
    public double? MatchScore(string account)
    {
        var entries = ReadEntries(account);
        if (entries == null || entries.Count == 0) return null;
        int comparable = 0, hit = 0;
        foreach (var e in entries)
        {
            if (IsNonDiscriminative(e.Rel)) continue;
            comparable++;
            var live = Path.Combine(rootDir, e.Rel);
            if (!File.Exists(live)) continue;
            try
            {
                if (string.Equals(CarrierProfiler.HashFile(live), e.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                    hit++;
            }
            catch { /* 被占用/读失败，跳过该条 */ }
        }
        return comparable == 0 ? (double?)null : (double)hit / comparable;
    }

    /// <summary>判断"当前 live 登录态是否等于该账号建档快照"（识别归属）：
    /// 相似度达到 <paramref name="minScore"/> 即视为匹配。默认 0.75——因为客户端每次运行
    /// 会改写噪声文件，全等判定无法用于"第二次建档/运行后识别"。
    /// </summary>
    public bool MatchesLive(string account, double minScore = 0.75)
        => MatchScore(account) is double s && s >= minScore;

    /// <summary>
    /// 计算"判别文件"集合：跨账号哈希互不相同、或仅部分账号拥有的非噪声载体文件。
    /// 识别只用判别文件评分——同机客户端共享的文件在账号间相同、无判别力，只会把
    /// 相似度向中间拉、降低区分度。
    /// </summary>
    public HashSet<string> BuildDiscriminantRels(IEnumerable<string> accounts)
    {
        var list = accounts as string[] ?? accounts.ToArray();
        if (list.Length == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // rel -> (出现过的哈希集合, 拥有该文件的账号集合)
        // 注意：元组是值类型，计数必须用引用类型（HashSet）记录，直接 int++ 只改副本、字典里恒为 0。
        var byRel = new Dictionary<string, (HashSet<string> Shas, HashSet<string> Accounts)>(StringComparer.OrdinalIgnoreCase);
        foreach (var acc in list)
        {
            var entries = ReadEntries(acc);
            if (entries == null) continue;
            foreach (var e in entries)
            {
                if (IsNonDiscriminative(e.Rel)) continue;
                if (!byRel.TryGetValue(e.Rel, out var t))
                    byRel[e.Rel] = t = (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                                        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                t.Shas.Add(e.Sha256Hex);
                t.Accounts.Add(acc);
            }
        }
        // 有判别力：所有账号都拥有但内容互不相同（Shas>1），
        // 或仅部分账号拥有（缺失本身即账号差异）。全部账号同内容则无判别力。
        return new HashSet<string>(
            byRel.Where(kv => kv.Value.Shas.Count > 1 || kv.Value.Accounts.Count < list.Length)
                 .Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 仅针对判别文件计算 live 与某账号的匹配度 0..1：该账号 meta 中属于
    /// <paramref name="discriminant"/> 的文件在 live 存在且哈希一致的命中比例。
    /// 判别文件都是"账号特异"文件，同账号命中应接近 1、其他账号应显著更低。
    /// </summary>
    public double? MatchScoreOnRels(string account, ISet<string> discriminant)
    {
        var entries = ReadEntries(account);
        if (entries == null || entries.Count == 0) return null;
        int total = 0, hit = 0;
        foreach (var e in entries)
        {
            if (!discriminant.Contains(e.Rel)) continue;
            total++;
            var live = Path.Combine(rootDir, e.Rel);
            if (!File.Exists(live)) continue;
            try
            {
                if (string.Equals(CarrierProfiler.HashFile(live), e.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                    hit++;
            }
            catch { /* 被占用/读失败，跳过该条 */ }
        }
        return total == 0 ? (double?)null : (double)hit / total;
    }

    public async Task RestoreAsync(string account, IEnumerable<string> fingerprint)
    {
        foreach (var rel in fingerprint)
            OverwriteEntry(AccountDir(account), rootDir, rel);
        await Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(string account, IEnumerable<string> fingerprint)
    {
        if (!File.Exists(MetaPath(account))) return false;
        foreach (var rel in ExpandFingerprint(rootDir, fingerprint))
        {
            var vaultFile = Path.Combine(AccountDir(account), rel);
            var liveFile = Path.Combine(rootDir, rel);
            if (!File.Exists(vaultFile) || !File.Exists(liveFile)) return false;
            if (!string.Equals(
                    CarrierProfiler.HashFile(vaultFile), CarrierProfiler.HashFile(liveFile),
                    StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return await Task.FromResult(true);
    }

    /// <summary>
    /// 账号名 → 安全的 vault 目录名：无效文件名字符替换为 _。
    /// . 不在 InvalidFileNameChars 里，必须单独拦截 "." 与 ".."，否则会逃逸出 vaultRoot。
    /// </summary>
    internal static string Sanitize(string account)
    {
        var bad = Path.GetInvalidFileNameChars();
        var name = new string(account.Select(c => bad.Contains(c) ? '_' : c).ToArray());
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") name = "untitled";
        return name;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
}
