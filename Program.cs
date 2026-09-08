using System.Runtime.InteropServices;
using TraeSwitch.Services;

namespace TraeSwitch;

internal static class Program
{
    /// <summary>单实例互斥锁名称（本地会话）。</summary>
    private const string MutexName = @"Local\TraeSwitch.SingleInstance";

    /// <summary>主窗口标题，用于跨进程定位并唤起已有窗口（须与 MainForm.Text 一致）。</summary>
    private const string MainWindowTitle = "TraeSwitch 账号冷切换助手";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在运行：唤起其主窗口后直接退出本实例（避免重复双击各自开窗）
            BringExistingWindowToFront();
            return;
        }

        ApplicationConfiguration.Initialize();
        var settings = new SettingsStore(CarrierDefaults.SettingsDir);
        // 首次启动须同意用户协议；不同意直接退出，同意后写入 settings 不再询问
        if (!settings.Data.EulaAccepted)
        {
            using (var eula = new EulaForm())
            {
                if (eula.ShowDialog() != DialogResult.OK)
                    return;
            }
            settings.Data.EulaAccepted = true;
            settings.Save();
        }
        Application.Run(new MainForm(settings));
    }

    /// <summary>找到已运行实例的主窗口，恢复显示并置前。</summary>
    private static void BringExistingWindowToFront()
    {
        IntPtr hwnd = FindWindow(null, MainWindowTitle);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 窗口可能已最小化到托盘（Hide 后 ShowInTaskbar=false），先显示再还原
        ShowWindow(hwnd, SW_SHOW);
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }
}
