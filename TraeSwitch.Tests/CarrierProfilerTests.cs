using TraeSwitch.Services;

namespace TraeSwitch.Tests;

public class CarrierProfilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "carrier_test_" + Guid.NewGuid().ToString("N"));

    public CarrierProfilerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Write(string rel, string text)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Fact]
    public void HashTree_跳过排除目录_只记录文件()
    {
        Write("aha\\state.json", "A");
        Write("Cache\\data_0", "noise");
        Write("top.txt", "B");

        var tree = CarrierProfiler.HashTree(_root, CarrierProfiler.DefaultExcludeDirs);

        Assert.Equal(2, tree.Count);
        Assert.Contains("aha/state.json", tree.Keys);
        Assert.DoesNotContain(tree.Keys, k => k.StartsWith("Cache/"));
    }

    [Fact]
    public void Diff_能找出新增_修改_删除()
    {
        Write("a.json", "A1");
        Write("b.json", "B");
        var before = CarrierProfiler.HashTree(_root, CarrierProfiler.DefaultExcludeDirs);

        File.WriteAllText(Path.Combine(_root, "a.json"), "A2"); // 修改
        File.Delete(Path.Combine(_root, "b.json"));            // 删除
        Write("c.json", "C");                                  // 新增
        var after = CarrierProfiler.HashTree(_root, CarrierProfiler.DefaultExcludeDirs);

        var changed = CarrierProfiler.DiffChangedPaths(before, after);
        Assert.Contains("a.json", changed);
        Assert.Contains("b.json", changed);
        Assert.Contains("c.json", changed);
    }
}
