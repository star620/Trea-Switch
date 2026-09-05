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

    [Fact]
    public void 新目录_使用默认路径()
    {
        var store = new SettingsStore(_dir);
        Assert.Equal(CarrierDefaults.DefaultUserDataDir, store.Data.RootDir);
        Assert.Equal(CarrierDefaults.DefaultProcessName, store.Data.ProcessName);
    }
}
