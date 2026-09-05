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
