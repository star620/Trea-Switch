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
