using TraeSwitch.Services;

namespace TraeSwitch.Tests;

/// <summary>切换守护行为测试：用 FakeClient 在 Launch 时改/不改 storage.json 模拟各场景。</summary>
public class SwitchGuardTests
{
    private const string StorageRel = "User\\globalStorage\\storage.json";

    private sealed class FakeClient : IClientController
    {
        public bool Running;
        public int Launches;
        public Action? OnLaunch;
        public void KillAll() => Running = false;
        public void Launch()
        {
            Launches++;
            Running = true;
            OnLaunch?.Invoke();
        }
        public bool IsRunning() => Running;
    }

    private sealed class Env : IDisposable
    {
        public string Root;
        public VaultService Vault;
        public string[] Fp = [StorageRel];

        /// <summary>账号名 A/B 建档（A 内容 contentA、B 内容 contentB），live 停在 B = 切换前状态。</summary>
        public Env(string nameA, string contentA, string nameB, string contentB)
        {
            Root = Path.Combine(Path.GetTempPath(), "guard_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "User", "globalStorage"));
            Vault = new VaultService(Root, Path.Combine(Root, "_vault"));
            Write(contentA);
            Vault.BackupAsync(nameA, Fp).GetAwaiter().GetResult();
            Write(contentB);
            Vault.BackupAsync(nameB, Fp).GetAwaiter().GetResult();
        }
        public string Storage => Path.Combine(Root, StorageRel);
        public void Write(string content) => File.WriteAllText(Storage, content);
        public string Read() => File.ReadAllText(Storage);
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }

    private static string Creds(string tag) =>
        $$"""{"iCubeAuthInfo://usertag":"u-{{tag}}","iCubeAuthInfo://icube.cloudide":"c-{{tag}}","extra":"x"}""";
    private static string NoCreds(string tag) =>
        $$"""{"extra":"{{tag}}"}""";

    private void SetTimeout(TimeSpan t) => SwitcherService.GuardTimeout = t;

    [Fact]
    public async Task 切换后客户端写入会话_判定Active()
    {
        SetTimeout(TimeSpan.FromSeconds(1.5));
        try
        {
            using var env = new Env("A", Creds("A"), "B", Creds("B"));
            var fake = new FakeClient();
            int writes = 0;
            fake.OnLaunch = () => { if (++writes == 1) env.Write(Creds("A-REFRESHED")); }; // 模拟启动后刷新会话
            var sw = new SwitcherService(env.Root, env.Vault, fake);

            var outcome = await sw.SwitchWithGuardAsync("A", env.Fp);

            Assert.Equal(SwitchOutcomeKind.Active, outcome.Kind);
            Assert.Contains("A-REFRESHED", env.Read()); // 客户端写的内容在
            Assert.Equal(1, fake.Launches);             // 未回滚
            Directory.Delete(outcome.RollbackDir!, true);
        }
        finally { SwitcherService.GuardTimeout = TimeSpan.FromSeconds(20); }
    }

    [Fact]
    public async Task 目标备份无凭据_判定回滚到切换前()
    {
        SetTimeout(TimeSpan.FromSeconds(1.5));
        try
        {
            using var env = new Env("A", NoCreds("A-STALE"), "B", Creds("B"));
            var before = env.Read(); // 切换前 live = B
            var fake = new FakeClient();
            int writes = 0;
            fake.OnLaunch = () => { if (++writes == 1) env.Write(NoCreds("A-WRITE")); };
            var sw = new SwitcherService(env.Root, env.Vault, fake);

            var outcome = await sw.SwitchWithGuardAsync("A", env.Fp);

            Assert.Equal(SwitchOutcomeKind.RolledBack, outcome.Kind);
            Assert.Equal(before, env.Read());  // 已回滚回切换前内容
            Assert.Equal(2, fake.Launches);    // 回滚后重启过一次
            Directory.Delete(outcome.RollbackDir!, true);
        }
        finally { SwitcherService.GuardTimeout = TimeSpan.FromSeconds(20); }
    }

    [Fact]
    public async Task 有凭据但无写入_判定NeedsConfirm不动live()
    {
        SetTimeout(TimeSpan.FromSeconds(1.2));
        try
        {
            using var env = new Env("A", Creds("A"), "B", Creds("B"));
            var fake = new FakeClient { OnLaunch = () => { } }; // 客户端起来但没写 storage.json
            var sw = new SwitcherService(env.Root, env.Vault, fake);

            var outcome = await sw.SwitchWithGuardAsync("A", env.Fp);

            Assert.Equal(SwitchOutcomeKind.NeedsConfirm, outcome.Kind);
            Assert.Equal(1, fake.Launches); // 不自动回滚
            Assert.Contains("u-A", env.Read()); // 已写回目标 A，未被回滚
            Directory.Delete(outcome.RollbackDir!, true);
        }
        finally { SwitcherService.GuardTimeout = TimeSpan.FromSeconds(20); }
    }

    [Fact]
    public async Task 客户端启动即崩_判定回滚到切换前()
    {
        SetTimeout(TimeSpan.FromSeconds(1.2));
        try
        {
            using var env = new Env("A", Creds("A"), "B", Creds("B"));
            var before = env.Read();
            var fake = new FakeClient();
            fake.OnLaunch = () => fake.Running = false; // 启动即退出
            var sw = new SwitcherService(env.Root, env.Vault, fake);

            var outcome = await sw.SwitchWithGuardAsync("A", env.Fp);

            Assert.Equal(SwitchOutcomeKind.RolledBack, outcome.Kind);
            Assert.Equal(before, env.Read());
            Assert.Equal(2, fake.Launches);
            Directory.Delete(outcome.RollbackDir!, true);
        }
        finally { SwitcherService.GuardTimeout = TimeSpan.FromSeconds(20); }
    }
}
