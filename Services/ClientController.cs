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
