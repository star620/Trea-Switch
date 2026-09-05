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
