using System.Text.Json;

namespace TraeSwitch.Services;

public sealed class AppSettingsData
{
    public List<string> Accounts { get; set; } = [];
    public string RootDir { get; set; } = "";
    public string ClientExe { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public List<string> Fingerprint { get; set; } = [];
    /// <summary>是否已同意首次启动的用户协议（EULA）。同意后不再弹出。</summary>
    public bool EulaAccepted { get; set; }
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
        Data = LoadData();
    }

    /// <summary>读配置；文件不存在、损坏或解析失败统一回退到默认值，避免启动即崩溃。</summary>
    private AppSettingsData LoadData()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                return JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath))
                    ?? new AppSettingsData();
            }
            catch
            {
                // 坏文件：回退默认值但不抛；下次 Save() 会用正常值覆盖它
            }
        }
        return new AppSettingsData
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
