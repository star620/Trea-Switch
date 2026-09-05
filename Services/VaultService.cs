using System.Text.Json;

namespace TraeSwitch.Services;

public sealed record VaultMeta(string Account, string CreatedUtc, List<VaultEntry> Entries);
public sealed record VaultEntry(string Rel, long Len, string Sha256Hex);

/// <summary>
/// 按账号把"载体文件"原样字节备份到 vaultRoot\&lt;Account&gt;\，
/// 支持恢复与哈希校验。只做字节拷贝，不解密。
/// </summary>
public sealed class VaultService(string rootDir, string vaultRoot)
{
    public string AccountDir(string account) => Path.Combine(vaultRoot, Sanitize(account));
    public string MetaPath(string account) => Path.Combine(AccountDir(account), "meta.json");

    public async Task BackupAsync(string account, IEnumerable<string> fingerprintRelPaths)
    {
        var dir = AccountDir(account);
        Directory.CreateDirectory(dir);
        var entries = new List<VaultEntry>();
        foreach (var rel in fingerprintRelPaths)
        {
            var src = Path.Combine(rootDir, rel);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            entries.Add(new VaultEntry(rel, new FileInfo(src).Length, CarrierProfiler.HashFile(src)));
        }
        var meta = new VaultMeta(account, DateTime.UtcNow.ToString("o"), entries);
        await File.WriteAllTextAsync(MetaPath(account), JsonSerializer.Serialize(meta, JsonOpts));
    }

    public async Task RestoreAsync(string account, IEnumerable<string> fingerprintRelPaths)
    {
        foreach (var rel in fingerprintRelPaths)
        {
            var src = Path.Combine(AccountDir(account), rel);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(rootDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
        await Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(string account, IEnumerable<string> fingerprintRelPaths)
    {
        if (!File.Exists(MetaPath(account))) return false;
        foreach (var rel in fingerprintRelPaths)
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
