using System.Text.Json;

namespace TraeSwitch.Services;

public sealed record VaultMeta(string Account, string CreatedUtc, List<VaultEntry> Entries);
public sealed record VaultEntry(string Rel, long Len, string Sha256Hex);

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

    public async Task BackupAsync(string account, IEnumerable<string> fingerprint)
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

    private static string Sanitize(string account)
    {
        var bad = Path.GetInvalidFileNameChars();
        return new string(account.Select(c => bad.Contains(c) ? '_' : c).ToArray());
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
}
