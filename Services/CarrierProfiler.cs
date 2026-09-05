using System.Security.Cryptography;

namespace TraeSwitch.Services;

/// <summary>
/// 目录树指纹：对指定根目录（排除噪声目录）逐文件算 SHA-256，
/// 通过两次快照 diff 找出"随账号切换而变化"的载体文件集合。
/// </summary>
public static class CarrierProfiler
{
    /// <summary>默认跳过的大体积/无关目录（相对名匹配任意层级）。</summary>
    public static readonly string[] DefaultExcludeDirs =
    [
        "Cache", "CachedData", "CachedConfigurations", "CachedProfilesData",
        "Code Cache", "GPUCache", "Crashpad", "DawnGraphiteCache", "DawnWebGPUCache",
        "ShaderCache", "Dictionaries", "blob_storage", "Service Worker",
        "Shared Dictionary", "logs", "Backups"
    ];

    /// <summary>扫描 root，返回 相对路径(反斜杠→正斜杠)→小写 SHA-256。</summary>
    public static Dictionary<string, string> HashTree(string root, IEnumerable<string> excludeDirs)
    {
        var ex = new HashSet<string>(excludeDirs, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                bool isDir = Directory.Exists(entry);
                var name = Path.GetFileName(entry);
                if (isDir)
                {
                    if (!ex.Contains(name)) stack.Push(entry);
                }
                else
                {
                    var rel = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    result[rel] = HashFile(entry);
                }
            }
        }
        return result;
    }

    public static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(sha.ComputeHash(fs));
    }

    /// <summary>返回两个快照间出现差异的相对路径（新增/修改/删除）。</summary>
    public static HashSet<string> DiffChangedPaths(
        IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, hash) in after)
        {
            if (!before.TryGetValue(rel, out var old) || !string.Equals(old, hash, StringComparison.OrdinalIgnoreCase))
                changed.Add(rel);
        }
        foreach (var rel in before.Keys)
        {
            if (!after.ContainsKey(rel)) changed.Add(rel);
        }
        return changed;
    }
}
