using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TraeSwitch.Services;

/// <summary>
/// 应用内自更新：从 GitHub Releases 查询最新正式版，下载 exe 到本地，
/// 并通过一个独立 cmd 脚本在旧进程退出后自动覆盖替换并重启新版。
/// </summary>
public static class UpdaterService
{
    public const string Repo = "star620/Trea-Switch";
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub REST API 对缺 User-Agent 的匿名请求会 403，必须带上
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"TraeSwitch/{CurrentVersion}");
        return h;
    }

    /// <summary>运行中程序的版本号（来自程序集，跟随 csproj &lt;Version&gt;）。</summary>
    public static Version CurrentVersion
        => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    public sealed record ReleaseInfo(string Tag, Version Version, string ExeUrl, long Size);

    /// <summary>远程 release 中发现比本地更新的版本时返回其信息；否则返回 null。</summary>
    public static async Task<ReleaseInfo?> GetNewerAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest",
            HttpCompletionOption.ResponseHeadersRead, ct);
        // 非 2xx（403 限流/无 UA、5xx 等）不静默当"无新版"，抛出让上层如实提示
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"更新检查失败：HTTP {(int)resp.StatusCode}");
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) ||
            !TryParseTag(tag.GetString() ?? "", out var ver))
            return null;
        if (ver <= CurrentVersion) return null;   // 本地已是最新（>= 远程）

        string? exeUrl = null; long size = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (string.Equals(a.GetProperty("name").GetString(), "TraeSwitch.exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeUrl = a.GetProperty("browser_download_url").GetString();
                    size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    break;
                }
            }
        }
        return exeUrl == null ? null : new ReleaseInfo(tag.GetString()!, ver, exeUrl, size);
    }

    /// <summary>把 "v1.2.3" / "1.2.3" / "1.2.3-beta" 解析为 Version；失败返回 false。</summary>
    public static bool TryParseTag(string tag, out Version ver)
    {
        ver = new Version();
        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
        var parts = s.Split('.');
        if (parts.Length < 2) return false;
        var items = new int[4];
        for (var i = 0; i < 4; i++)
        {
            // 如 "1.2.3-beta"：第 4 段可能带后缀，只取前一个减号
            var part = i < parts.Length ? parts[i].Split('-', '+')[0] : "0";
            if (!int.TryParse(part, out items[i])) return false;
        }
        ver = new Version(items[0], items[1], items[2], items[3]);
        return true;
    }

    /// <summary>下载 exe 到 destDir\TraeSwitch.update.exe 并返回完整路径；sink 用于汇报进度。</summary>
    public static async Task<string> DownloadAsync(string url, string destDir,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        var dest = Path.Combine(destDir, "TraeSwitch.update.exe");
        await using var fs = File.Create(dest);
        var buf = new byte[81920];
        long done = 0; int read;
        while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report((int)(done * 100 / total));
        }
        return dest;
    }

    /// <summary>
    /// 用独立 cmd 脚本异步应用更新：反复重试直到旧进程退出（文件解锁）后
    /// 覆盖目标 exe、清理临时文件、启动新版。调用通常紧接着退出本程序。
    /// </summary>
    public static void ApplyInBackground(string updateExe, string targetExe)
    {
        var bat = Path.Combine(Path.GetTempPath(), $"apply_update_{Guid.NewGuid():N}.bat");
        // 用 set "VAR=path" 语法（值不含引号），再以 "%VAR%" 引用，避免引号嵌套导致 copy 永远失败
        var lines = new[]
        {
            "@echo off",
            // 文件以 UTF-8(no BOM) 写入，chcp 65001 让 cmd 按 UTF-8 解析，避免中文安装路径乱码导致 copy 永久失败
            "chcp 65001 >nul",
            $"set \"SRC={updateExe}\"",
            $"set \"DST={targetExe}\"",
            ":loop",
            "copy /y \"%SRC%\" \"%DST%\" >nul 2>&1",
            "if not errorlevel 1 goto launch",
            "timeout /t 2 /nobreak >nul",
            "goto loop",
            ":launch",
            "del /q \"%SRC%\" 2>nul",
            "del /q \"%~f0\" 2>nul",
            "start \"\" \"%DST%\"",
            "exit"
        };
        File.WriteAllLines(bat, lines);
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi);
    }
}