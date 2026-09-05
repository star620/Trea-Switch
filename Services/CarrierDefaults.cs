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
