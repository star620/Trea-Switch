# TraeSwitch (TraeWork CN 多账号冷切换) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 TraeWork CN 记住 N 个账号的登录态，一键"退出当前→恢复目标账号→重开"，免手机验证码。

**Architecture:** 独立 WinForms/.NET9 小工具。核心三层：`CarrierProfiler`（切号前后文件 diff → 得出"登录态载体指纹"）、`VaultService`（按账号原样字节备份/恢复/校验载体文件）、`SwitcherService`（结束进程→写回→拉起→回滚）。载体真实路径由 Phase 0（顺延执行的只读定位脚本）确认；在确认前所有载体操作都用"假载体"（临时目录）开发与单测，UI 与 Pilot 待真载体接入。

**Tech Stack:** .NET 9 WinForms (net9.0-windows)、xunit 2.9.2、System.Text.Json、SHA-256 流式哈希；进程控制用 `System.Diagnostics.Process`。

**Spec:** `docs/superpowers/specs/2026-09-05-trae-switch-design.md`

---

## 文件结构

```
TraeSwitch/
├── TraeSwitch.csproj                 # WinExe 主程序（Services + UI）
├── Program.cs                        # 入口
├── AppStyle.cs                       # 视觉常量（参照 TraeCheckin 深色+蓝）
├── MainForm.cs                       # 账号列表 + 建档/切换/校验 操作
├── Services/
│   ├── CarrierProfiler.cs            # 目录树哈希 + diff → 载体指纹
│   ├── CarrierDefaults.cs            # 默认用户数据根目录/排除目录/进程名/客户端 exe
│   ├── VaultService.cs               # 备份/恢复/校验/元数据
│   ├── SwitcherService.cs            # 冷切换编排 + 回滚
│   ├── ClientController.cs           # 真进程控制（kill/launch/IsRunning）
│   └── SettingsStore.cs              # settings.json（账号/路径/指纹）
├── TraeSwitch.Tests/
│   ├── TraeSwitch.Tests.csproj
│   ├── CarrierProfilerTests.cs
│   ├── VaultServiceTests.cs
│   ├── SwitcherServiceTests.cs
│   └── SettingsStoreTests.cs
└── TraeSwitch.Probe/                 # Phase 0 一次性只读定位脚本（Console）
    ├── TraeSwitch.Probe.csproj
    └── Program.cs
```

约定：主 csproj 用默认 glob，但必须显式 `<Compile Remove>` 掉 `TraeSwitch.Tests\**` 与 `TraeSwitch.Probe\**`，否则测试/探针源码会被编进主程序。测试不依赖 `InternalsVisibleTo`（服务类全部 public）。所有服务不直接 new 具体依赖，构造注入，便于假载体/假进程测试。

---

### Task 1: 脚手架（主程序 + 测试工程）

**Files:**
- Create: `TraeSwitch.csproj`
- Create: `Program.cs`
- Create: `MainForm.cs`（占位空窗体，Task 6 再充实）
- Create: `AppStyle.cs`
- Create: `TraeSwitch.Tests/TraeSwitch.Tests.csproj`
- Create: `TraeSwitch.Tests/GlobalUsing.cs`
- Test: `TraeSwitch.Tests/SmokeTests.cs`

- [ ] **Step 1: 写主工程 csproj**

`TraeSwitch.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>TraeSwitch</RootNamespace>
    <AssemblyName>TraeSwitch</AssemblyName>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
    <OutputPath>bin\TraeSwitch\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <!-- 排除测试子项目与探针工程，避免默认 glob 把它们的源码编进主程序 -->
  <ItemGroup>
    <Compile Remove="TraeSwitch.Tests\**" />
    <Compile Remove="TraeSwitch.Probe\**" />
    <EmbeddedResource Remove="TraeSwitch.Tests\**" />
    <EmbeddedResource Remove="TraeSwitch.Probe\**" />
    <Content Remove="TraeSwitch.Tests\**" />
    <Content Remove="TraeSwitch.Probe\**" />
    <None Remove="TraeSwitch.Tests\**" />
    <None Remove="TraeSwitch.Probe\**" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 写入口、占位窗体和样式常量**

`Program.cs`：

```csharp
namespace TraeSwitch;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
```

`MainForm.cs`（占位，Task 6 充实）：

```csharp
namespace TraeSwitch;

public class MainForm : Form
{
    public MainForm()
    {
        Text = "TraeSwitch 账号冷切换助手";
        ClientSize = new Size(880, 560);
        BackColor = AppStyle.ContentBack;
        StartPosition = FormStartPosition.CenterScreen;
    }
}
```

`AppStyle.cs`：

```csharp
namespace TraeSwitch;

public static class AppStyle
{
    public static readonly Color SideBack = Color.FromArgb(0x1E, 0x29, 0x3B);
    public static readonly Color Accent = Color.FromArgb(0x3B, 0x82, 0xF6);
    public static readonly Color ContentBack = Color.FromArgb(0xF1, 0xF5, 0xF9);
    public static readonly Color TextDark = Color.FromArgb(0x0F, 0x17, 0x2A);
}
```

- [ ] **Step 3: 写测试工程**

`TraeSwitch.Tests/TraeSwitch.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\TraeSwitch.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

`TraeSwitch.Tests/SmokeTests.cs`：

```csharp
using System.Drawing;
using TraeSwitch;

namespace TraeSwitch.Tests;

public class SmokeTests
{
    [Fact]
    public void 样式常量非空()
    {
        Assert.Equal(Color.FromArgb(0x1E, 0x29, 0x3B), AppStyle.SideBack);
    }
}
```

- [ ] **Step 4: 跑测试确认绿**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: 通过 1 个测试。

- [ ] **Step 5: 提交**

```bash
git add TraeSwitch.csproj Program.cs MainForm.cs AppStyle.cs TraeSwitch.Tests
git commit -m "chore: scaffold TraeSwitch WinForms app with test project"
```

---

### Task 2: CarrierProfiler（目录树哈希 + diff）

**Files:**
- Create: `Services/CarrierProfiler.cs`
- Test: `TraeSwitch.Tests/CarrierProfilerTests.cs`

- [ ] **Step 1: 写失败测试**

`TraeSwitch.Tests/CarrierProfilerTests.cs`：

```csharp
using TraeSwitch;
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
        Assert.Contains("aha\\state.json", tree.Keys);
        Assert.DoesNotContain(tree.Keys, k => k.StartsWith("Cache\\"));
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
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: FAIL（`CarrierProfiler` 不存在）。

- [ ] **Step 3: 写最小实现**

`Services/CarrierProfiler.cs`：

```csharp
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>扫描 root，返回 相对路径(用反斜杠)→小写 SHA-256。</summary>
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
    public static HashSet<string> DiffChangedPaths(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
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
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: 全绿。

- [ ] **Step 5: 提交**

```bash
git add Services/CarrierProfiler.cs TraeSwitch.Tests/CarrierProfilerTests.cs
git commit -m "feat: carrier tree hashing and diff"
```

---

### Task 3: VaultService（备份 / 恢复 / 校验）

**Files:**
- Create: `Services/VaultService.cs`
- Test: `TraeSwitch.Tests/VaultServiceTests.cs`

载体路径来自一份 `Fingerprint`（相对路径集合，阶段上先由用户/Phase 0 提供，缺省为空=档案空但流程可测）。vault 布局：`vaultRoot\<Account>\meta.json` + 按指纹相对路径镜像存储。

- [ ] **Step 1: 写失败测试**

`TraeSwitch.Tests/VaultServiceTests.cs`：

```csharp
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
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: FAIL。

- [ ] **Step 3: 写最小实现**

`Services/VaultService.cs`：

```csharp
using System.Security.Cryptography;
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
        var metaPath = MetaPath(account);
        if (!File.Exists(metaPath)) return false;
        foreach (var rel in fingerprintRelPaths)
        {
            var vaultFile = Path.Combine(AccountDir(account), rel);
            var liveFile = Path.Combine(rootDir, rel);
            if (!File.Exists(vaultFile) || !File.Exists(liveFile)) return false;
            if (!string.Equals(CarrierProfiler.HashFile(vaultFile), CarrierProfiler.HashFile(liveFile), StringComparison.OrdinalIgnoreCase))
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
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: 全绿。

- [ ] **Step 5: 提交**

```bash
git add Services/VaultService.cs TraeSwitch.Tests/VaultServiceTests.cs
git commit -m "feat: per-account vault backup/restore/verify"
```

---

### Task 4: SwitcherService（冷切换编排 + 回滚）

**Files:**
- Create: `Services/ClientController.cs`
- Create: `Services/SwitcherService.cs`
- Test: `TraeSwitch.Tests/SwitcherServiceTests.cs`

切换把"当前 live 载体文件"先复制到临时回滚目录，再写回目标账号 vault，最后拉起客户端；任一步失败回滚。真实进程控制走 `ClientController`（接口化，测试注入假实现）。

- [ ] **Step 1: 写失败测试**

`TraeSwitch.Tests/SwitcherServiceTests.cs`：

```csharp
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
        // 结束前 live 恢复到“最近切到”的账号内容（这里演示最后备份的是 accounts[^1]）
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
        Assert.False(fake.IsRunning() == false); // Launch 后应为运行中
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
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: FAIL。

- [ ] **Step 3: 写最小实现**

`Services/ClientController.cs`：

```csharp
using System.Diagnostics;

namespace TraeSwitch.Services;

public interface IClientController
{
    void KillAll();
    void Launch();
    bool IsRunning();
}

/// <summary>真实进程控制：按进程名结束全部实例并重新拉起客户端。</summary>
public sealed class ClientController(string processName, string exePath) : IClientController
{
    public void KillAll()
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(10_000); } catch { /* 已退出则忽略 */ }
        }
    }

    public void Launch()
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new FileNotFoundException("找不到客户端：" + exePath);
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }

    public bool IsRunning() => Process.GetProcessesByName(processName).Length > 0;
}
```

`Services/SwitcherService.cs`：

```csharp
namespace TraeSwitch.Services;

/// <summary>
/// 冷切换编排：结束进程 → 当前 live 载体做临时回滚快照 → 写回目标账号 vault → 拉起客户端。
/// restore/launch 任一步失败则回滚到切换前内容。
/// </summary>
public sealed class SwitcherService(string rootDir, VaultService vault, IClientController client)
{
    public async Task SwitchToAsync(string account, IEnumerable<string> fingerprint)
    {
        var rels = fingerprint.ToArray();
        client.KillAll();

        // 1) 当前 live 状态快照到临时目录（回滚用）
        var rollback = Path.Combine(Path.GetTempPath(), "traeswitch_rollback_" + Guid.NewGuid().ToString("N"));
        foreach (var rel in rels)
        {
            var src = Path.Combine(rootDir, rel);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(rollback, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }

        try
        {
            // 2) 写回目标账号
            await vault.RestoreAsync(account, rels);
        }
        catch
        {
            Rollback(rollback, rels);
            throw;
        }

        try
        {
            // 3) 拉起
            client.Launch();
        }
        catch
        {
            Rollback(rollback, rels);
            try { client.Launch(); } catch { /* 尽力恢复启动 */ }
            throw;
        }
        finally
        {
            try { Directory.Delete(rollback, true); } catch { /* 忽略清理失败 */ }
        }
    }

    private void Rollback(string rollback, string[] rels)
    {
        foreach (var rel in rels)
        {
            var src = Path.Combine(rollback, rel);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(rootDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: 全绿（含：切换顺序、Kill 抛错传播、Launch 失败回滚）。

- [ ] **Step 5: 提交**

```bash
git add Services/ClientController.cs Services/SwitcherService.cs TraeSwitch.Tests/SwitcherServiceTests.cs
git commit -m "feat: cold-switch orchestration with rollback"
```

---

### Task 5: SettingsStore（账号与路径记忆）

**Files:**
- Create: `Services/SettingsStore.cs`
- Test: `TraeSwitch.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: 写失败测试**

`TraeSwitch.Tests/SettingsStoreTests.cs`：

```csharp
using TraeSwitch.Services;

namespace TraeSwitch.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "settings_test_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void 保存再读取_账号与路径保持()
    {
        var store = new SettingsStore(_dir);
        store.Data.Accounts.Add("A");
        store.Data.Accounts.Add("B");
        store.Data.RootDir = @"C:\fake\root";
        store.Data.ClientExe = @"D:\TRAE SOLO CN\TRAE SOLO CN.exe";
        store.Save();

        var again = new SettingsStore(_dir);
        Assert.Equal(2, again.Data.Accounts.Count);
        Assert.Contains("A", again.Data.Accounts);
        Assert.Equal(@"C:\fake\root", again.Data.RootDir);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: FAIL。

- [ ] **Step 3: 写最小实现**

`Services/CarrierDefaults.cs`：

```csharp
namespace TraeSwitch.Services;

public static class CarrierDefaults
{
    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TraeSwitch");

    public static string DefaultUserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TRAE SOLO CN");

    public static string DefaultClientExe => @"D:\TRAE SOLO CN\TRAE SOLO CN.exe";

    public static string DefaultProcessName => "TRAE SOLO CN";
}
```

`Services/SettingsStore.cs`（最终完整实现）：

```csharp
using System.Text.Json;

namespace TraeSwitch.Services;

public sealed class AppSettingsData
{
    public List<string> Accounts { get; set; } = [];
    public string RootDir { get; set; } = "";
    public string ClientExe { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public List<string> Fingerprint { get; set; } = [];
}

/// <summary>settings.json 读写；目录不存在自动创建。文件不存在时用默认值。</summary>
public sealed class SettingsStore
{
    public AppSettingsData Data { get; private set; }
    public string FilePath { get; }

    public SettingsStore(string dir)
    {
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "settings.json");
        Data = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath)) ?? new AppSettingsData()
            : new AppSettingsData
            {
                RootDir = CarrierDefaults.DefaultUserDataDir,
                ClientExe = CarrierDefaults.DefaultClientExe,
                ProcessName = CarrierDefaults.DefaultProcessName,
                Fingerprint = []
            };
    }

    public void Save()
        => File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test TraeSwitch.Tests/TraeSwitch.Tests.csproj --nologo -v q`
Expected: 全绿。

- [ ] **Step 5: 提交**

```bash
git add Services/SettingsStore.cs Services/CarrierDefaults.cs TraeSwitch.Tests/SettingsStoreTests.cs
git commit -m "feat: persist accounts and client paths in settings"
```

---

### Task 6: 主界面（账号列表 + 建档/切换/校验）

**Files:**
- Modify: `MainForm.cs`（替换占位）
- Modify: `Program.cs`（传 SettingsStore 单例）

UI 从简可测：左侧账号列表（`ListBox`），右侧当前客户端路径说明 + 操作按钮：`建档(备份当前账号)`、`切换到选中账号`、`校验`、`删除`。真实"学习载体指纹"走 Phase 0 定位后再接（按钮先隐藏，见 Task 7）。所有服务调用以日志区（`TextBox` readonly）输出结果。视觉用 `AppStyle` 常量，标题区高度加大防 DPI 截断。

- [ ] **Step 1: 改 Program.cs 注入设置**

`Program.cs`：

```csharp
using TraeSwitch.Services;

namespace TraeSwitch;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = new SettingsStore(CarrierDefaults.SettingsDir);
        Application.Run(new MainForm(settings));
    }
}
```

- [ ] **Step 2: 写 MainForm 核心逻辑（建档/切换/校验），保留界面方法内联**

`MainForm.cs`（完整替换占位；`FlowLayoutPanel` 保证确定布局避免 Dock 叠放乱序）：

```csharp
using TraeSwitch.Services;

namespace TraeSwitch;

public class MainForm : Form
{
    private readonly SettingsStore _settings;
    private readonly ListBox _accounts;
    private readonly TextBox _log;
    private readonly VaultService _vault;

    public MainForm(SettingsStore settings)
    {
        _settings = settings;
        var root = _settings.Data.RootDir;
        var vaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraeSwitch", "vault");
        _vault = new VaultService(root, vaultRoot);

        Text = "TraeSwitch 账号冷切换助手";
        ClientSize = new Size(900, 600);
        BackColor = AppStyle.ContentBack;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.RowCount = 2;
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _accounts = new ListBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10) };
        _accounts.DataSource = _settings.Data.Accounts;
        panel.Controls.Add(_accounts, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        right.RowCount = 2;
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(BuildInfo(), 0, 0);
        _log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
        right.Controls.Add(_log, 0, 1);
        panel.Controls.Add(right, 1, 0);

        var bottom = BuildActions();
        panel.Controls.Add(bottom, 0, 1);
        panel.SetColumnSpan(bottom, 2);
        Controls.Add(panel);

        RefreshAccounts();
    }

    private Control BuildInfo()
    {
        var lbl = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"用户数据目录：{_settings.Data.RootDir}\n客户端：{_settings.Data.ClientExe}",
            Font = new Font("Microsoft YaHei UI", 9),
            ForeColor = AppStyle.TextDark,
            AutoSize = false
        };
        return lbl;
    }

    private Control BuildActions()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(MakeBtn("建档（备份当前账号）", async () => await DoBackupAsync()));
        row.Controls.Add(MakeBtn("切换到选中账号", async () => await DoSwitchAsync()));
        row.Controls.Add(MakeBtn("校验选中账号", async () => await DoVerifyAsync()));
        row.Controls.Add(MakeBtn("删除选中账号", () => DoDelete()));
        row.Controls.Add(MakeBtn("刷新列表", RefreshAccounts));
        return row;
    }

    private Button MakeBtn(string text, Action onClick) => new() { Text = text, AutoSize = true, Height = 34, BackColor = AppStyle.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 6, 10, 6) };

    private void RefreshAccounts()
    {
        var sel = _accounts.SelectedItem as string;
        _accounts.DataSource = null;
        _accounts.DataSource = _settings.Data.Accounts;
        if (sel != null) _accounts.SelectedItem = sel;
    }

    private async Task DoBackupAsync()
    {
        var name = PromptAccount("建档：输入当前登录的账号名");
        if (name == null) return;
        if (!_settings.Data.Accounts.Contains(name)) _settings.Data.Accounts.Add(name);
        await _vault.BackupAsync(name, _settings.Data.Fingerprint);
        _settings.Save();
        Log($"已备份账号 {name}（载体文件 {_settings.Data.Fingerprint.Count} 个）。提示：请确认当前客户端登录的确实是该账号。");
        RefreshAccounts();
    }

    private async Task DoSwitchAsync()
    {
        var name = _accounts.SelectedItem as string;
        if (name == null) { Log("请先选中目标账号。"); return; }
        if (_settings.Data.Fingerprint.Count == 0) { Log("尚未配置载体指纹：请先运行 Phase 0 定位脚本，把变化的相对路径填到 settings.json 的 Fingerprint 后重试。"); return; }
        var client = new ClientController(_settings.Data.ProcessName, _settings.Data.ClientExe);
        var sw = new SwitcherService(_settings.Data.RootDir, _vault, client);
        try
        {
            Log($"开始切换到 {name}…");
            await sw.SwitchToAsync(name, _settings.Data.Fingerprint);
            Log($"已切换并启动客户端。请确认界面登录的是 {name}。");
        }
        catch (Exception ex)
        {
            Log("切换失败：" + ex.Message);
        }
    }

    private async Task DoVerifyAsync()
    {
        var name = _accounts.SelectedItem as string;
        if (name == null) { Log("请先选中账号。"); return; }
        var ok = await _vault.VerifyAsync(name, _settings.Data.Fingerprint);
        Log(ok ? $"账号 {name} vault 与 live 一致。" : $"账号 {name} vault 校验失败：客户端可能改写了登录态，请重新登录该账号后点「建档」更新备份。");
    }

    private void DoDelete()
    {
        var name = _accounts.SelectedItem as string;
        if (name == null) return;
        _settings.Data.Accounts.Remove(name);
        _settings.Save();
        RefreshAccounts();
        Log($"已从列表移除 {name}（vault 目录建议手动删除以清空登录态字节）。");
    }

    private string? PromptAccount(string title)
    {
        using var dlg = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(320, 110), StartPosition = FormStartPosition.CenterParent
        };
        var tb = new TextBox { Left = 12, Top = 16, Width = 296 };
        var ok = new Button { Text = "确定", Left = 12, Top = 56, Width = 140, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 168, Top = 56, Width = 140, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
        return dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text) ? tb.Text.Trim() : null;
    }

    private void Log(string msg) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
}
```

- [ ] **Step 3: 编译确认**

Run: `dotnet build TraeSwitch.csproj -c Release --nologo -v q`
Expected: 0 错误。

- [ ] **Step 4: 手动冒烟（假载体）**

Run: `dotnet run --project TraeSwitch.csproj`（或运行 `bin\TraeSwitch\TraeSwitch.exe`）。
在没配置 Fingerprint 前，点"切换到选中账号"应提示"尚未配置载体指纹"；点"建档"能把账号加入列表并写空 vault。Expected: 无崩溃、日志区输出正确。

- [ ] **Step 5: 提交**

```bash
git add MainForm.cs Program.cs
git commit -m "feat: account management UI with backup/switch/verify"
```

---

### Task 7: Phase 0 一次性定位脚本（顺延执行）+ Pilot 说明

**Files:**
- Create: `TraeSwitch.Probe/TraeSwitch.Probe.csproj`
- Create: `TraeSwitch.Probe/Program.cs`
- Create: `docs/PILOT.md`

定位不读文件内容，只用 相对路径+大小+最后写入时间 快速快照（全树跳过缓存噪声）；用户在"某次本来就要切号"时执行 `snap` → 切号 → 退出 → `diff`。

- [ ] **Step 1: 写探针工程**

`TraeSwitch.Probe/TraeSwitch.Probe.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>TraeSwitch.Probe</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TraeSwitch.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 写探针逻辑**

`TraeSwitch.Probe/Program.cs`：

```csharp
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
                if (isDir) { if (!ex.Contains(name)) stack.Push(entry); }
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
        var changed = new List<string>();
        changed.AddRange(a.Except(b));
        changed.AddRange(b.Except(a).Where(x => !a.Contains(x)));
        Console.WriteLine($"共 {changed.Count} 个文件在切号前后变化：");
        foreach (var line in changed.OrderBy(x => x))
        {
            var rel = line.Split('|')[0];
            Console.WriteLine("  " + rel);
        }
        Console.WriteLine("结论：把这些相对路径填到 TraeSwitch settings.json 的 Fingerprint 后，即可在 UI 中启用真实切换。");
    }
}
```

- [ ] **Step 3: 编译探针**

Run: `dotnet build TraeSwitch.Probe/TraeSwitch.Probe.csproj -c Release --nologo -v q`
Expected: 0 错误。

- [ ] **Step 4: 写 Pilot 执行说明**

`docs/PILOT.md`：记录流程——每次切号（预计发生在你自然要结束本对话/换号时）：① 完全退出客户端 → `Probe snap 1` → 正常登录另一账号 → 完全退出 → `Probe snap 2` → `Probe diff 1`；把结果中"每账号登录后保持稳定、切号即变"的相对路径填入 settings `Fingerprint`；在 UI 用假数据建档 2 账号连续切 1 周，观察是否触发服务端风控/需要重验证；稳定后再放开给 3-4 账号用户。并把真实载体验证结果反馈回 spec 第 4 节。

- [ ] **Step 5: 提交**

```bash
git add TraeSwitch.Probe docs/PILOT.md
git commit -m "docs: one-shot carrier locator probe and pilot instructions"
```

---

## 自审说明（Self-Review）

- **Spec 覆盖**：Phase 0（Task 7）、脚手架（Task 1）、CarrierProfiler/VaultService/SwitcherService（Task 2-4）、设置持久化（Task 5）、UI（Task 6）、Pilot 与降级（PILOT.md）。"每账号归档一次/免 OTP 切换/回滚/完整性校验"均有对应任务。
- **已知待办**（不阻塞开发，因需真实登录事件）：
  - Phase 0 真实载体定位 → 填充 `settings.json` 的 `Fingerprint`；
  - 真 `ClientController.KillAll/Launch` 行为需在真实客户端上冒烟（Task 4 仅测编排与回滚，进程层留给 Pilot）。
- **类型一致性**：`CarrierProfiler.HashTree/DiffChangedPaths`、`VaultService(root,vault)`、`SettingsStore(dir)`、`SwitcherService(root,vault,client)`、`AppSettingsData` 字段命名在各 Task 间一致。
