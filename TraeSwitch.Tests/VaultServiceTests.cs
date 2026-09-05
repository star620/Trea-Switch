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

    [Fact]
    public async Task 目录条目_备份整树_恢复先删再写回_校验能检测新增孤儿()
    {
        // leveldb 式目录：含 CURRENT/MANIFEST + 滚动 .log，模拟真实载体目录
        WriteRoot("leveldb\\CURRENT", "MANIFEST-000001");
        WriteRoot("leveldb\\MANIFEST-000001", "m1");
        WriteRoot("leveldb\\000001.log", "LOG-A");
        WriteRoot("leveldb\\000001.ldb", "DATA-A");
        var fp = new[] { "leveldb" };
        var svc = new VaultService(_root, _vault);

        await svc.BackupAsync("A", fp);
        Assert.True(File.Exists(Path.Combine(_vault, "A", "leveldb", "000001.log")));

        // 客户端"运行"产生滚动新文件
        WriteRoot("leveldb\\000002.log", "LOG-A2");
        Assert.False(await svc.VerifyAsync("A", fp));

        // 恢复后孤儿 000002.log 应被清除，目录与备份一致
        await svc.RestoreAsync("A", fp);
        Assert.False(File.Exists(Path.Combine(_root, "leveldb", "000002.log")));
        Assert.Equal("LOG-A", File.ReadAllText(Path.Combine(_root, "leveldb", "000001.log")));
        Assert.True(await svc.VerifyAsync("A", fp));
    }

    [Fact]
    public async Task 目录与文件混合指纹_文件随账号变_恢复还原()
    {
        WriteRoot("sess\\000001.log", "SESS-A");
        WriteRoot("cfg.db", "CFG-A");
        var fp = new[] { "sess", "cfg.db" };
        var svc = new VaultService(_root, _vault);

        await svc.BackupAsync("A", fp);
        File.WriteAllText(Path.Combine(_root, "sess", "000001.log"), "SESS-B");
        File.WriteAllText(Path.Combine(_root, "cfg.db"), "CFG-B");

        Assert.False(await svc.VerifyAsync("A", fp));

        await svc.RestoreAsync("A", fp);
        Assert.Equal("SESS-A", File.ReadAllText(Path.Combine(_root, "sess", "000001.log")));
        Assert.Equal("CFG-A", File.ReadAllText(Path.Combine(_root, "cfg.db")));
    }
}
