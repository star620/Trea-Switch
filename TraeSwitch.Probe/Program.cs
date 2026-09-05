using TraeSwitch.Services;

namespace TraeSwitch.Probe;

static class Program
{
    private static readonly string SnapDir = Path.Combine(Path.GetTempPath(), "traeswitch_probe");
    private static readonly string Root = CarrierDefaults.DefaultUserDataDir;

    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        switch (mode)
        {
            case "snap": Snap(args.Length > 1 ? args[1] : "1"); break;
            case "diff": Diff(args.Length > 1 ? args[1] : "1"); break;
            default:
                Console.WriteLine("用法：");
                Console.WriteLine("  TraeSwitch.Probe snap 1   # 切号前执行（客户端已完全退出）");
                Console.WriteLine("  TraeSwitch.Probe snap 2   # 切号完成、客户端再次完全退出后执行");
                Console.WriteLine("  TraeSwitch.Probe diff 1   # 对比 snap1 与 snap2，输出变化文件");
                break;
        }
    }

    static void Snap(string tag)
    {
        Directory.CreateDirectory(SnapDir);
        var lines = new List<string>();
        var ex = new HashSet<string>(CarrierProfiler.DefaultExcludeDirs, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var isDir = Directory.Exists(entry);
                var name = Path.GetFileName(entry);
                if (isDir)
                {
                    if (!ex.Contains(name)) stack.Push(entry);
                }
                else
                {
                    var fi = new FileInfo(entry);
                    var rel = Path.GetRelativePath(Root, entry);
                    lines.Add($"{rel}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}");
                }
            }
        }
        File.WriteAllLines(Path.Combine(SnapDir, $"snap{tag}.txt"), lines);
        Console.WriteLine($"snap{tag} 完成：{lines.Count} 个文件 → {SnapDir}\\snap{tag}.txt");
    }

    static void Diff(string tag)
    {
        var f1 = Path.Combine(SnapDir, $"snap{tag}.txt");
        var f2 = Path.Combine(SnapDir, $"snap{tag + 1}.txt");
        if (!File.Exists(f1) || !File.Exists(f2))
        {
            Console.WriteLine("缺少快照文件，请先按顺序执行 snap 1 与 snap 2。");
            return;
        }
        var a = File.ReadAllLines(f1).ToHashSet();
        var b = File.ReadAllLines(f2).ToHashSet();
        var keys = new HashSet<string>(a.Concat(b).Select(line => line.Split('|')[0]));
        var changed = keys
            .Where(k => !a.Any(l => l.StartsWith(k + "|")) || !b.Any(l => l.StartsWith(k + "|"))
                        || a.First(l => l.StartsWith(k + "|")) != b.First(l => l.StartsWith(k + "|")))
            .OrderBy(x => x)
            .ToList();
        Console.WriteLine($"共 {changed.Count} 个文件在切号前后变化：");
        foreach (var rel in changed)
            Console.WriteLine("  " + rel);
        Console.WriteLine("结论：把这些相对路径填到 TraeSwitch settings.json 的 Fingerprint 后，即可在 UI 中启用真实切换。");
    }
}
