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

    [Fact]
    public void settings文件损坏_回退默认值_不抛异常()
    {
        // 预写坏文件（截断/乱写的 JSON 都会让 Deserialize 抛 JsonException）
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ 这不是合法JSON, accounts: [");
        File.WriteAllText(path, "\ufeff" + "{ broken" + new string('x', 500));

        // 构造不应抛异常，且回退为可用默认值
        var store = new SettingsStore(_dir);
        Assert.NotNull(store.Data);
        Assert.Equal(CarrierDefaults.DefaultUserDataDir, store.Data.RootDir);
        Assert.Equal(CarrierDefaults.DefaultProcessName, store.Data.ProcessName);

        // 且仍可安全保存（用正常值覆盖坏文件）
        store.Data.Accounts.Add("A");
        store.Save();
        var again = new SettingsStore(_dir);
        Assert.Contains("A", again.Data.Accounts);
    }
}
