using TraeSwitch.Services;

namespace TraeSwitch.Tests;

public class VaultServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vault_test_" + Guid.NewGuid().ToString("N"));
    private readonly string _vault;

    public VaultServiceTests()
    {
        Directory.CreateDirectory(_root);
        _vault = Path.Combine(_root, "_vault");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void WriteRoot(string rel, string text)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Fact]
    public async Task 备份后修改live文件_校验能检测差异_恢复能还原()
    {
        WriteRoot("aha\\state.json", "SESSION-A");
        var fp = new[] { "aha/state.json" };
        var svc = new VaultService(_root, _vault);

        await svc.BackupAsync("A", fp);
        File.WriteAllText(Path.Combine(_root, "aha", "state.json"), "SESSION-A-CORRUPTED");

        Assert.False(await svc.VerifyAsync("A", fp));

        await svc.RestoreAsync("A", fp);
        Assert.Equal("SESSION-A", File.ReadAllText(Path.Combine(_root, "aha", "state.json")));
        Assert.True(await svc.VerifyAsync("A", fp));
    }

    [Fact]
    public async Task 未备份账号_校验返回false()
    {
        var svc = new VaultService(_root, _vault);
        Assert.False(await svc.VerifyAsync("NOPE", new[] { "aha/state.json" }));
    }
}
