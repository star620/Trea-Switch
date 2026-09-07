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

    [Fact]
    public async Task 建档返回文件数_GetInfo返回时间与数量()
    {
        WriteRoot("sess\\000001.log", "SESS-A");
        WriteRoot("sess\\000002.log", "SESS-A2");
        WriteRoot("cfg.db", "CFG-A");
        var svc = new VaultService(_root, _vault);

        var count = await svc.BackupAsync("A", new[] { "sess", "cfg.db" });
        Assert.Equal(3, count);

        var info = svc.GetInfo("A");
        Assert.NotNull(info);
        Assert.Equal(3, info!.EntryCount);
        Assert.NotNull(info.CreatedLocal);
        Assert.Null(svc.GetInfo("NOPE"));
    }

    [Fact]
    public async Task MatchesLive_未建档返回false_live与快照一致为true_篡改后为false()
    {
        WriteRoot("sess\\000001.log", "SESS-A");
        WriteRoot("cfg.db", "CFG-A");
        var fp = new[] { "sess", "cfg.db" };
        var svc = new VaultService(_root, _vault);

        Assert.False(svc.MatchesLive("A")); // 未建档

        await svc.BackupAsync("A", fp);
        Assert.True(svc.MatchesLive("A")); // 建档后 live 即快照

        File.WriteAllText(Path.Combine(_root, "cfg.db"), "CFG-A-CHANGED");
        Assert.False(svc.MatchesLive("A")); // 2 文件中 1 个被改 → 命中 50% < 0.75
    }

    [Fact]
    public async Task MatchesLive_live多出滚动文件仍匹配_删除快照文件则不匹配()
    {
        // leveldb 场景：建档后客户端运行会多出 000002.log（滚动新文件），识别仍应判定归属
        WriteRoot("leveldb\\000001.log", "LOG-A");
        WriteRoot("leveldb\\CURRENT", "C1");
        var svc = new VaultService(_root, _vault);
        await svc.BackupAsync("A", new[] { "leveldb" });

        WriteRoot("leveldb\\000002.log", "NEW-ROLLED");
        Assert.True(svc.MatchesLive("A")); // 多出的滚动文件不判负

        File.Delete(Path.Combine(_root, "leveldb", "000001.log"));
        Assert.False(svc.MatchesLive("A")); // 快照数据文件被删 → 命中 0% → 不属于该账号
    }

    [Fact]
    public async Task MatchScore_忽略leveldb噪声文件_运行改写LOG仍接近全匹配()
    {
        // 真实场景：客户端每次运行都会改写 LOG / LOG.old，但它们与账号无关，评分应忽略
        WriteRoot("leveldb\\000001.log", "DATA-A");
        WriteRoot("leveldb\\CURRENT", "C1");
        WriteRoot("leveldb\\MANIFEST-000001", "m1");
        WriteRoot("leveldb\\LOG", "RUN1");
        WriteRoot("leveldb\\LOG.old", "OLD1");
        var svc = new VaultService(_root, _vault);
        await svc.BackupAsync("A", new[] { "leveldb" });

        // 模拟再次运行：LOG 内容变化、新增滚动文件，数据文件不变
        File.WriteAllText(Path.Combine(_root, "leveldb", "LOG"), "RUN2-CHANGED");
        File.WriteAllText(Path.Combine(_root, "leveldb", "LOG.old"), "OLD2-CHANGED");
        WriteRoot("leveldb\\000002.log", "ROLLED");

        var score = svc.MatchScore("A");
        Assert.NotNull(score);
        Assert.True(score!.Value > 0.9, $"期望忽略噪声后近全匹配，实际 {score:P0}");
    }

    [Fact]
    public async Task MatchScore_数据文件被改写_评分显著下降()
    {
        WriteRoot("sess\\000001.log", "SESS-A");
        WriteRoot("sess\\000002.log", "SESS-A2");
        WriteRoot("cfg.db", "CFG-A");
        var svc = new VaultService(_root, _vault);
        await svc.BackupAsync("A", new[] { "sess", "cfg.db" });
        Assert.Equal(1.0, svc.MatchScore("A"));

        // 改掉全部 3 个数据文件
        File.WriteAllText(Path.Combine(_root, "sess", "000001.log"), "SESS-B");
        File.WriteAllText(Path.Combine(_root, "sess", "000002.log"), "SESS-B2");
        File.WriteAllText(Path.Combine(_root, "cfg.db"), "CFG-B");
        Assert.Equal(0.0, svc.MatchScore("A"));

        // 无 meta 的账号返回 null
        Assert.Null(svc.MatchScore("NOPE"));
    }

    [Fact]
    public async Task 判别集_排除共享文件与运行痕迹_只保留账号特异文件()
    {
        // 两账号共享部分：同内容 leveldb 文件（无判别力）、以及不同建档时刻的 config.db（运行痕迹，必须排除）
        WriteRoot("shared\\000001.ldb", "SAME");          // 两账号同内容 → 非判别
        WriteRoot("config.db", "RUN-A");                   // 运行痕迹，建档时刻不同 → 必须排除
        WriteRoot("onlyA\\000600.log", "A-TOKEN");         // 仅 A 有 → 判别
        WriteRoot("onlyB\\000603.log", "B-TOKEN");         // 仅 B 有 → 判别
        var svc = new VaultService(_root, _vault);
        await svc.BackupAsync("A", new[] { "shared", "config.db", "onlyA" });
        await svc.BackupAsync("B", new[] { "shared", "config.db", "onlyB" });

        var disc = svc.BuildDiscriminantRels(new[] { "A", "B" });
        Assert.Contains(@"onlyA\000600.log", disc);
        Assert.Contains(@"onlyB\000603.log", disc);
        Assert.DoesNotContain(@"shared\000001.ldb", disc);      // 全账号同内容
        Assert.DoesNotContain("config.db", disc);               // 运行痕迹被排除
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/../b")]
    [InlineData("A:B")]
    public void Sanitize_危险账号名_不逃逸vault根目录(string account)
    {
        var name = VaultService.Sanitize(account);
        // 结果必须是 vaultRoot 下的单一合法子目录名（不含分隔符，不会是 "." / ".."）
        Assert.NotEqual("..", name);
        Assert.NotEqual(".", name);
        Assert.False(name.Contains(Path.DirectorySeparatorChar));
        Assert.False(name.Contains(Path.AltDirectorySeparatorChar));
        Assert.Equal(name, Path.GetFileName(name));
    }

    [Theory]
    [InlineData("用户0612021494")]
    [InlineData("A")]
    public void Sanitize_正常账号名_原样保留(string account)
    {
        Assert.Equal(account, VaultService.Sanitize(account));
    }
}
