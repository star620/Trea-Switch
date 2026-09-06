namespace TraeSwitch.Services;

/// <summary>切换守护结果。</summary>
public enum SwitchOutcomeKind
{
    /// <summary>探测到会话写入，判定已进入目标账号。</summary>
    Active,
    /// <summary>明显没进入（凭据缺失/客户端未起来），已自动回滚到切换前的 live 并重启。</summary>
    RolledBack,
    /// <summary>信号不足（凭据在但未见写入/暂时读不到），需要用户确认是否真的进入。</summary>
    NeedsConfirm
}

/// <summary>
/// 切换结果；RollbackDir 保留"切换前 live"快照，MainForm 处理完需删除；
/// 需要回滚时调用 <see cref="SwitcherService.RollbackSwitchAsync"/>。
/// </summary>
public sealed record SwitchOutcome(SwitchOutcomeKind Kind, string? RollbackDir, string[] Rels, string Account);

/// <summary>
/// 冷切换编排：结束进程 → 当前 live 载体做临时回滚快照 → 写回目标账号 vault → 拉起客户端。
/// restore/launch 任一步失败则回滚到切换前内容。Fingerprint 条目支持文件或目录。
/// </summary>
public sealed class SwitcherService(string rootDir, VaultService vault, IClientController client)
{
    /// <summary>守护轮询上限；internal 以便单测缩短。</summary>
    internal static TimeSpan GuardTimeout = TimeSpan.FromSeconds(20);

    public async Task SwitchToAsync(string account, IEnumerable<string> fingerprint)
    {
        var rels = fingerprint.ToArray();
        client.KillAll();

        // 1) 当前 live 状态快照到临时目录（回滚用），目录条目整树快照
        var rollback = Path.Combine(Path.GetTempPath(), "traeswitch_rollback_" + Guid.NewGuid().ToString("N"));
        foreach (var rel in rels)
            VaultService.OverwriteEntry(rootDir, rollback, rel);

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

    /// <summary>
    /// 带守护的切换：保留"切换前 live"快照直到守护判定结束。
    /// 启动后轮询 storage.json：写入过 = 云会话在工作（Active）；
    /// 凭据缺失/进程没起来 = 明显失败，自动回滚并重启（RolledBack）；
    /// 凭据在但没观察到写入 = 需要用户确认（NeedsConfirm）。
    /// </summary>
    public async Task<SwitchOutcome> SwitchWithGuardAsync(
        string account, IEnumerable<string> fingerprint, Action<TimeSpan>? progress = null)
    {
        var rels = fingerprint.ToArray();
        client.KillAll();

        var rollback = Path.Combine(Path.GetTempPath(), "traeswitch_rollback_" + Guid.NewGuid().ToString("N"));
        foreach (var rel in rels)
            VaultService.OverwriteEntry(rootDir, rollback, rel);

        try { await vault.RestoreAsync(account, rels); }
        catch
        {
            Rollback(rollback, rels);
            try { Directory.Delete(rollback, true); } catch { /* 忽略清理失败 */ }
            throw;
        }

        var storage = AuthProbe.StoragePath(rootDir);
        var baseline = AuthProbe.GetStamp(storage); // 客户端已关，指纹稳定

        try { client.Launch(); }
        catch
        {
            // 已恢复 live；本路径异常会向上抛（outcome 为 null，MainForm 不会来收尾），
            // 需在此自行清掉临时回滚目录，避免 %TEMP% 滞留。
            Rollback(rollback, rels);
            try { Directory.Delete(rollback, true); } catch { /* 忽略清理失败 */ }
            try { client.Launch(); } catch { /* 尽力恢复 */ }
            throw;
        }

        // 轮询：等客户端起来，并看它是否真的写入了 storage.json（云会话在工作的迹象）
        bool runningSeen = false, changed = false;
        var started = DateTime.UtcNow;
        var deadline = started.Add(GuardTimeout);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(400);
            progress?.Invoke(DateTime.UtcNow - started);
            if (!client.IsRunning()) continue;
            runningSeen = true;
            if (AuthProbe.StampChanged(baseline, AuthProbe.GetStamp(storage)))
            {
                changed = true;
                break;
            }
        }

        var creds = AuthProbe.HasAuthCreds(storage);
        SwitchOutcomeKind kind;
        if (creds == false || !runningSeen)
            kind = SwitchOutcomeKind.RolledBack;      // 确定没登录 / 进程没起来
        else if (creds == true && changed)
            kind = SwitchOutcomeKind.Active;           // 写了会话且凭据齐全
        else
            kind = SwitchOutcomeKind.NeedsConfirm;     // 凭据在但没写入迹象 / 暂时读不到

        if (kind == SwitchOutcomeKind.RolledBack)
            DoRollback(rollback, rels);

        return new SwitchOutcome(kind, rollback, rels, account);
    }

    /// <summary>守护判定为 NeedsConfirm 且用户选择回滚时调用。</summary>
    public Task RollbackSwitchAsync(SwitchOutcome outcome)
    {
        if (outcome.RollbackDir is { } dir)
            DoRollback(dir, outcome.Rels);
        return Task.CompletedTask;
    }

    private void DoRollback(string rollback, string[] rels)
    {
        try { client.KillAll(); } catch { /* 忽略 */ }
        Rollback(rollback, rels);
        try { client.Launch(); } catch { /* 尽力恢复启动 */ }
    }

    private void Rollback(string rollback, string[] rels)
    {
        foreach (var rel in rels)
            VaultService.OverwriteEntry(rollback, rootDir, rel);
    }
}
