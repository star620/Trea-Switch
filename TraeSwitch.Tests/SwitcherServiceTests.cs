using TraeSwitch.Services;

namespace TraeSwitch.Tests;

public class SwitcherServiceTests
{
    private sealed class FakeClient : IClientController
    {
        public bool Running = true;
        public int Launches;
        public Action? OnKillAll;
        public Action? OnLaunch;
        public void KillAll() { OnKillAll?.Invoke(); Running = false; }
        public void Launch() { OnLaunch?.Invoke(); Running = true; Launches++; }
        public bool IsRunning() => Running;
    }

    private static (string Root, VaultService Vault, string[] Fp) Setup(params string[] accounts)
    {
        var root = Path.Combine(Path.GetTempPath(), "switch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "aha"));
        File.WriteAllText(Path.Combine(root, "aha", "state.json"), accounts[0]);
        var vault = new VaultService(root, Path.Combine(root, "_vault"));
        vault.BackupAsync(accounts[0], new[] { "aha/state.json" }).GetAwaiter().GetResult();
        foreach (var acc in accounts.Skip(1))
        {
            File.WriteAllText(Path.Combine(root, "aha", "state.json"), acc);
            vault.BackupAsync(acc, new[] { "aha/state.json" }).GetAwaiter().GetResult();
        }
        // 结束前 live 恢复到"最近切到"的账号内容
        File.WriteAllText(Path.Combine(root, "aha", "state.json"), accounts[^1]);
        return (root, vault, new[] { "aha/state.json" });
    }

    [Fact]
    public async Task 切换_先关再恢复目标_再启动()
    {
        var (root, vault, fp) = Setup("A", "B");
        var fake = new FakeClient();
        var sw = new SwitcherService(root, vault, fake);

        await sw.SwitchToAsync("A", fp);

        Assert.Equal("A", File.ReadAllText(Path.Combine(root, "aha", "state.json")));
        Assert.Equal(1, fake.Launches);
        Assert.True(fake.IsRunning());
    }

    [Fact]
    public async Task 结束进程失败_异常向上抛且不改live()
    {
        var (root, vault, fp) = Setup("A", "B");
        var fake = new FakeClient { OnKillAll = () => throw new InvalidOperationException("结束进程失败") };
        var sw = new SwitcherService(root, vault, fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sw.SwitchToAsync("A", fp));
        Assert.Equal("B", File.ReadAllText(Path.Combine(root, "aha", "state.json")));
    }

    [Fact]
    public async Task 启动失败_回滚为切换前内容()
    {
        var (root, vault, fp) = Setup("A", "B");
        var fake = new FakeClient { OnLaunch = () => throw new InvalidOperationException("启动失败") };
        var sw = new SwitcherService(root, vault, fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sw.SwitchToAsync("A", fp));
        Assert.Equal("B", File.ReadAllText(Path.Combine(root, "aha", "state.json"))); // 回滚回切换前
    }
}
