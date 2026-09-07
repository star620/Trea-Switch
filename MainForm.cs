using TraeSwitch.Services;

namespace TraeSwitch;

public class MainForm : Form
{
    private readonly SettingsStore _settings;
    private readonly ListView _accounts;
    private readonly TextBox _log;
    private readonly VaultService _vault;
    private readonly ClientController _client;
    /// <summary>本次运行是否已做过一次"自动刷新备份"（避免反复尝试）。</summary>
    private bool _autoRefreshedOnce;
    /// <summary>切换按钮引用，切换期间禁用，防止并发重复切换（会互相 KillAll/Restore 冲突）。</summary>
    private Button _switchBtn = null!;
    /// <summary>是否正在切换中。</summary>
    private bool _switching;

    public MainForm(SettingsStore settings)
    {
        _settings = settings;
        var root = _settings.Data.RootDir;
        var vaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraeSwitch", "vault");
        _vault = new VaultService(root, vaultRoot);
        _client = new ClientController(_settings.Data.ProcessName, _settings.Data.ClientExe);

        Text = "TraeSwitch 账号冷切换助手";
        ClientSize = new Size(960, 620);
        BackColor = AppStyle.ContentBack;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        panel.RowCount = 2;
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _accounts = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            ShowItemToolTips = true,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Columns =
            {
                new ColumnHeader { Text = "账号", Width = 150 },
                new ColumnHeader { Text = "建档时间", Width = 130 },
                new ColumnHeader { Text = "载体文件", Width = 66, TextAlign = HorizontalAlignment.Right },
                new ColumnHeader { Text = "状态", Width = 120 }
            }
        };
        _accounts.DoubleClick += (_, _) => DoVerifyAsync();
        panel.Controls.Add(_accounts, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        right.RowCount = 2;
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(BuildInfo(), 0, 0);
        _log = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = Color.White
        };
        right.Controls.Add(_log, 0, 1);
        panel.Controls.Add(right, 1, 0);

        var bottom = BuildActions();
        panel.Controls.Add(bottom, 0, 1);
        panel.SetColumnSpan(bottom, 2);
        Controls.Add(panel);

        RefreshAccounts();
    }

    private Control BuildInfo()
    {
        var lb = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"用户数据目录：{_settings.Data.RootDir}\n客户端：{_settings.Data.ClientExe}",
            Font = new Font("Microsoft YaHei UI", 9),
            ForeColor = AppStyle.TextDark
        };
        return lb;
    }

    private Control BuildActions()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false
        };
        row.Controls.Add(MakeBtn("建档（自动识别+一键）", async () => await DoBackupAsync()));
        _switchBtn = MakeBtn("切换到选中账号", async () => await DoSwitchAsync());
        row.Controls.Add(_switchBtn);
        row.Controls.Add(MakeBtn("校验选中账号", DoVerifyAsync));
        row.Controls.Add(MakeBtn("删除选中账号", DoDelete));
        row.Controls.Add(MakeBtn("刷新列表", RefreshAccounts));
        row.Controls.Add(MakeBtn("检查更新", () => _ = CheckUpdateAsync(silentWhenLatest: false)));
        return row;
    }

    /// <summary>窗体显示后静默检查一次更新（有新版才提示，不打扰启动）。</summary>
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await CheckUpdateAsync(silentWhenLatest: true);
    }

    /// <summary>
    /// 查询远程最新版并安装。silentWhenLatest=true（启动自动）时，无新版则静默不干扰；
    /// 反之（手动点按钮）会明确提示"已是最新"。发现新版则弹窗确认后下载、退出并自动替换。
    /// </summary>
    private async Task CheckUpdateAsync(bool silentWhenLatest)
    {
        // 联网检查/下载/安装进行中禁止再次触发，避免并发下载相同新版本
        if (_checkingUpdate) { Log("正在获取或安装更新，请稍候；完成后可再次检查。"); return; }
        _checkingUpdate = true;
        try { await CheckUpdateCoreAsync(silentWhenLatest); }
        finally { _checkingUpdate = false; }
    }

    private async Task CheckUpdateCoreAsync(bool silentWhenLatest)
    {
        Log("正在检查更新…");
        UpdaterService.ReleaseInfo? rel = null;
        try { rel = await UpdaterService.GetNewerAsync(); }
        catch (Exception ex) { Log("检查更新失败：" + ex.Message); return; }
        if (rel == null)
        {
            Log(silentWhenLatest
                ? $"自动检查更新：已是最新（v{UpdaterService.CurrentVersion}）。"
                : $"已是最新版本（v{UpdaterService.CurrentVersion}）。");
            return;
        }

        Log($"发现新版本 {rel.Tag}（当前 v{UpdaterService.CurrentVersion}）。");
        var r = MessageBox.Show(
            $"发现新版本 {rel.Tag}\n（当前 v{UpdaterService.CurrentVersion}，约 {Math.Max(1, (long)(rel.Size / 1048576.0))} MB）\n\n" +
            "是否下载并自动安装？",
            "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (r != DialogResult.Yes) { Log("已取消更新。"); return; }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) { Log("无法定位当前程序路径，更新中止。"); return; }
        var destDir = Path.GetDirectoryName(currentExe)!;

        Log("开始下载新版…（请稍候）");
        try
        {
            var p = new Progress<int>(pct => { if (pct / 10 != _lastUpdatePct) { _lastUpdatePct = pct / 10; Log($"  下载 {pct}%…"); } });
            var update = await Task.Run(() => UpdaterService.DownloadAsync(rel.ExeUrl, destDir, p));
            Log("下载完成：即将退出并自动替换安装。");
            UpdaterService.ApplyInBackground(update, currentExe);
            // 必须彻底退出进程以释放目标 exe 文件锁，否则后台脚本 copy 永远失败（update.exe 残留、不替换、不重启）
            await Task.Delay(400);   // 给后台 cmd 一点启动缓冲
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log("更新失败：" + ex.Message);
        }
    }

    private int _lastUpdatePct = -1;
    private bool _checkingUpdate;

    private Button MakeBtn(string text, Action onClick)
    {
        var btn = new Button
        {
            Text = text, AutoSize = true, Height = 34,
            BackColor = AppStyle.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 6, 10, 6)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private string? SelectedAccount => _accounts.SelectedItems.Count > 0
        ? _accounts.SelectedItems[0].Text
        : null;

    /// <summary>
    /// 刷新账号列表：每行显示 账号 / 建档时间 / 载体文件数 / 状态；
    /// 悬停任意行显示完整明细（含 vault 路径与识别说明）。
    /// 状态列在客户端退出时才做 live 相似度识别"当前账号"；运行中不做（避免读到被占用/写一半的文件）。
    /// </summary>
    private void RefreshAccounts()
    {
        var sel = SelectedAccount;
        _accounts.BeginUpdate();
        _accounts.Items.Clear();
        var clientRunning = _client.IsRunning();
        // 客户端退出时才计算各账号 live 匹配度（运行中读文件不可靠）
        var scores = clientRunning ? null : ScoreAllAccounts();
        string? current = scores is { Count: > 0 }
            ? (scores[0].Score >= 0.7 && (scores.Count == 1 || scores[0].Score - scores[1].Score >= 0.2)
                ? scores[0].Account : null)
            : null;
        foreach (var acc in _settings.Data.Accounts)
        {
            var info = _vault.GetInfo(acc);
            double? score = scores?.FirstOrDefault(s => s.Account == acc).Score;
            var status = info == null ? "未建档"
                : clientRunning ? "客户端运行中"
                : acc == current ? "当前账号 ✓" : "已建档";

            var it = new ListViewItem(acc);
            it.SubItems.Add(info?.CreatedLocal?.ToString("yyyy-MM-dd HH:mm") ?? "—");
            it.SubItems.Add(info?.EntryCount.ToString() ?? "—");
            it.SubItems.Add(status);
            it.ToolTipText = BuildRowTooltip(acc, info, status, clientRunning, score);
            _accounts.Items.Add(it);
        }
        _accounts.EndUpdate();
        if (sel != null)
            foreach (ListViewItem i in _accounts.Items)
                if (i.Text == sel) { i.Selected = true; break; }

        // 快照自动刷新：客户端已退出且识别出当前账号时，静默用当前会话刷新其备份（内容确有变化才刷）。
        if (!clientRunning && current != null && !_autoRefreshedOnce)
        {
            _autoRefreshedOnce = true;
            _ = TryAutoRefreshAsync(current);
        }
    }

    /// <summary>
    /// 当前 live 会话已确认属于 <paramref name="account"/> 且与旧备份不同时，自动重新建档。
    /// 客户端关闭后的"会话保鲜"：每次工具打开/刷新都尽量让该账号备份 = 最近一次有效会话。
    /// </summary>
    private async Task TryAutoRefreshAsync(string account)
    {
        try
        {
            var fp = _settings.Data.Fingerprint;
            if (fp.Count == 0) return;
            if (await _vault.VerifyAsync(account, fp))
            {
                Log($"「{account}」备份与当前会话一致，无需刷新。");
                return;
            }
            var count = await _vault.BackupAsync(account, fp);
            Log($"已用当前会话自动刷新「{account}」的备份（载体 {count} 个）。");
            RefreshAccounts();
        }
        catch (Exception ex)
        {
            Log("自动刷新备份失败：" + ex.Message);
        }
    }

    /// <summary>悬停行提示：列出该账号的建档详情与状态含义，行长文本不因窗口宽度被截断。</summary>
    private string BuildRowTooltip(string acc, VaultInfo? info, string status, bool clientRunning, double? score)
    {
        var vaultDir = _vault.AccountDir(acc);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"账号：{acc}");
        if (info == null)
        {
            sb.AppendLine("状态：未建档");
            sb.AppendLine("说明：该账号还没有登录态备份。");
            sb.AppendLine("操作：确认客户端登录该账号后点「建档（自动识别+一键）」即可完成建档。");
            return sb.ToString();
        }
        sb.AppendLine($"建档时间：{info.CreatedLocal:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"载体文件：{info.EntryCount} 个");
        sb.AppendLine($"状态：{status}");
        if (score is double sc) sb.AppendLine($"live 相似度：{sc:P0}（越高越可能是当前账号）");
        if (clientRunning)
        {
            sb.AppendLine("说明：客户端正在运行，为避免读到被占用/写入中的文件，");
            sb.AppendLine("      暂不比对当前登录账号。完全退出客户端后点「刷新列表」");
            sb.AppendLine("      即可识别当前账号（显示为「当前账号 ✓」）。");
        }
        else if (status.StartsWith("当前账号"))
        {
            sb.AppendLine("说明：live 登录态与该账号备份高度一致，当前客户端登录的就是它。");
        }
        else
        {
            sb.AppendLine("说明：当前会话与该账号备份不一致。若该账号是最近重新登录的，");
            sb.AppendLine("      其备份已过期（凭据每次登录都会轮换），对该账号再点一次");
            sb.AppendLine("      「建档」即可用当前会话更新备份。切换场景下应显示「当前账号 ✓」。");
        }
        sb.AppendLine($"vault：{vaultDir}");
        return sb.ToString();
    }

    /// <summary>
    /// 对全部已建档账号计算 live 匹配度，返回按分数降序的列表。
    /// 评分只用"判别文件"（账号间内容不同的载体文件）；同机共享文件无判别力，
    /// 若全部计入会把各账号分数都拉向中间、降低区分度。
    /// </summary>
    private List<(string Account, double Score)> ScoreAllAccounts()
    {
        var accs = _settings.Data.Accounts;
        var discriminant = _vault.BuildDiscriminantRels(accs);
        var result = new List<(string Account, double Score)>();
        foreach (var acc in accs)
            if (_vault.MatchScoreOnRels(acc, discriminant) is double s)
                result.Add((acc, s));
        return result.OrderByDescending(x => x.Score).ToList();
    }

    /// <summary>
    /// 识别当前 live 归属的账号：取最高分账号，要求分数达标且显著领先第二名。
    /// 领先幅度不足视为不确定（避免 A/B 分数接近时误判）。返回 null 表示无法唯一确定。
    /// </summary>
    private string? IdentifyLiveAccount(out List<(string Account, double Score)> scores)
    {
        scores = ScoreAllAccounts();
        if (scores.Count == 0) return null;
        var best = scores[0];
        if (best.Score < 0.7) return null;
        if (scores.Count > 1 && best.Score - scores[1].Score < 0.2) return null; // 与次高太接近
        return best.Account;
    }

    /// <summary>
    /// 一键建档：客户端运行则询问后自动退出 → 按相似度评分自动识别当前登录账号
    /// （分数达标且唯一领先才自动沿用其名；否则要求输入**新**账号名，绝不允许默认覆盖）→
    /// 备份 → 建档后立即校验并回显载体文件数。
    /// </summary>
    private async Task DoBackupAsync()
    {
        if (_client.IsRunning())
        {
            var r = MessageBox.Show(
                "检测到 TRAE SOLO CN 正在运行。建档需要完全退出客户端以解除登录态文件占用。\n\n是否自动退出客户端后继续？",
                "一键建档", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
            {
                Log("已取消建档：请先完全退出客户端（托盘也退出）再试，或允许自动退出。");
                return;
            }
            Log("正在退出客户端（含托盘驻留）…");
            _client.KillAll();
            Log("客户端已退出。");
        }
        else
        {
            Log("客户端未在运行，可直接建档。");
        }

        // 评分识别：打印各账号相似度便于诊断，达标且唯一领先才自动沿用
        var identified = IdentifyLiveAccount(out var scores);
        foreach (var (acc, s) in scores)
            Log($"  · 与「{acc}」相似度 {s:P0}");
        if (identified == null && scores.Count > 0)
            Log("  （相似度过低或互相接近：多半是当前账号重新登录过、旧备份已过期；请在弹窗中点选该账号以更新备份。）");

        string? name;
        if (identified != null)
        {
            name = identified;
            Log($"识别到当前登录账号为「{name}」，将更新其备份。");
        }
        else if (scores.Count > 0)
        {
            // 相似度过低/相近：说明该账号可能重新登录过、旧备份已过期。
            // 把每个账号的相似度写进弹窗，用户对照即可一键点选（不手输、无默认选中）。
            var scoreLines = string.Join("\n",
                scores.Select(s => $"  · 「{s.Account}」相似度 {s.Score:P0}"));
            name = PromptSelectAccount(
                "无法自动判定当前会话属于哪个账号——多半是它重新登录过、旧备份已过期。\n" +
                "请在下方点选当前登录的账号（将用本次会话更新其备份）；点「取消」则改为录入新账号名：\n\n" + scoreLines);
            if (name == null) return;
        }
        else
        {
            name = PromptNewAccount();
            if (name == null) return;
            // 防误覆盖：输入的名字若已存在，要求二次确认
            if (_settings.Data.Accounts.Contains(name))
            {
                var c = MessageBox.Show(
                    $"「{name}」已是已有账号，建档将覆盖其现有备份。\n\n如果这是为了更新该账号备份，请确认当前客户端登录的确实就是它，再点「是」。",
                    "覆盖确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (c != DialogResult.Yes) { Log("已取消建档。"); return; }
            }
        }

        try
        {
            if (!_settings.Data.Accounts.Contains(name)) _settings.Data.Accounts.Add(name);
            var count = await _vault.BackupAsync(name, _settings.Data.Fingerprint);
            var ok = await _vault.VerifyAsync(name, _settings.Data.Fingerprint);
            _settings.Save();
            RefreshAccounts();
            Log($"建档成功：{name}（载体文件 {count} 个），建档后校验 {(ok ? "通过" : "未通过，请重试")}。");
            if (identified == null)
                Log($"提示：已把当前账号按账号「{name}」建档，下次切换将免验证码进入该账号。");
        }
        catch (Exception ex)
        {
            Log("建档失败：" + ex.Message);
        }
    }

    private async Task DoSwitchAsync()
    {
        if (_switching) { Log("已在切换中，请等待当前切换完成。"); return; }
        var name = SelectedAccount;
        if (name == null) { Log("请先在列表中选中目标账号。"); return; }
        if (_settings.Data.Fingerprint.Count == 0)
        {
            Log("尚未配置载体指纹：请先运行 Phase 0 定位脚本，把变化的相对路径填到 settings.json 的 Fingerprint 后重试。");
            return;
        }
        var sw = new SwitcherService(_settings.Data.RootDir, _vault, _client);
        SwitchOutcome? outcome = null;
        _switching = true;
        _switchBtn.Enabled = false;
        int lastReported = 0;
        try
        {
            Log($"开始切换到 {name}…");
            Log($"正在等待客户端进入账号（最长约 {SwitcherService.GuardTimeout.TotalSeconds:0} 秒），客户端会自动打开，请稍候…");
            outcome = await sw.SwitchWithGuardAsync(name, _settings.Data.Fingerprint,
                progress: elapsed =>
                {
                    int sec = (int)elapsed.TotalSeconds;
                    if (sec >= 4 && sec % 2 == 0 && sec != lastReported)
                    {
                        lastReported = sec;
                        Log($"仍在等待客户端写入会话…（已 {sec} 秒 / 最长 {SwitcherService.GuardTimeout.TotalSeconds:0} 秒）");
                    }
                });
            switch (outcome.Kind)
            {
                case SwitchOutcomeKind.Active:
                    Log($"已切换并启动客户端：探测到会话写入，应已进入 {name}。");
                    Log("提示：确认进入后正常使用，退出客户端时点「刷新列表」，会自动用本次会话刷新备份。");
                    break;
                case SwitchOutcomeKind.RolledBack:
                    Log($"未能确认进入 {name}（疑似会话过期或客户端未能启动），已自动回滚到切换前的账号并重启客户端。");
                    Log("处理：如需切换到该账号，请先在其上登录一次并「建档」更新会话备份。");
                    break;
                case SwitchOutcomeKind.NeedsConfirm:
                    var r = MessageBox.Show(
                        "客户端已启动，但 20 秒内未观察到登录会话写入。\n\n" +
                        $"若界面已正常进入「{name}」，请点「是」；\n" +
                        "否则将回滚到切换前的账号（并重启客户端）。",
                        "确认切换结果", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes)
                    {
                        Log($"已切换并启动客户端（经确认进入 {name}）。");
                    }
                    else
                    {
                        await sw.RollbackSwitchAsync(outcome);
                        Log("已按确认回滚到切换前的账号并重启客户端。");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log("切换失败：" + ex.Message);
        }
        finally
        {
            _switchBtn.Enabled = true;
            _switching = false;
            if (outcome?.RollbackDir is { } dir)
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* 忽略清理失败 */ }
            }
            RefreshAccounts();
        }
    }

    private void DoVerifyAsync()
    {
        var name = SelectedAccount;
        if (name == null) { Log("请先在列表中选中账号。"); return; }
        if (_client.IsRunning())
        {
            Log("无法校验：TRAE SOLO CN 正在运行，登录态文件被占用。请先完全退出客户端再点「校验」。");
            return;
        }
        var info = _vault.GetInfo(name);
        if (info == null) { Log("该账号尚未建档，无需校验。"); return; }

        // 用与建档一致的判别识别来判定"当前 live 属于谁"，避免单账号严格比对在
        // LevelDB 滚动后把"不是当前账号"和"过期"全误报成"校验失败"。
        var current = IdentifyLiveAccount(out _);
        if (current == null)
        {
            Log($"当前 live 无法匹配任何已建档案（通常是重新登录过、旧备份已过期）；「{name}」的备份未受影响，无需重新建档。");
            return;
        }
        if (current == name)
        {
            Log($"「{name}」为当前登录账号，vault 与 live 一致（载体 {info.EntryCount} 个，建档于 {info.CreatedLocal:yyyy-MM-dd HH:mm}）。");
        }
        else
        {
            Log($"「{name}」不是当前登录账号（当前为「{current}」），备份未受影响、无需重建。要校验它请先切到该账号。");
        }
    }

    private void DoDelete()
    {
        var name = SelectedAccount;
        if (name == null) return;
        var r = MessageBox.Show($"确定从列表移除 {name}？\n\nvault 备份目录中的登录态字节不会被自动删除，如需彻底清除请手动删除对应目录。",
            "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        _settings.Data.Accounts.Remove(name);
        _settings.Save();
        RefreshAccounts();
        Log($"已从列表移除 {name}（vault 目录建议手动删除以清空登录态字节）。");
    }

    /// <summary>
    /// 输入**新**账号名弹窗（建档识别不到时用）：纯文本框，不提供已有账号列表，
    /// 从源头杜绝"默认选中已有账号导致误覆盖"。返回 null 表示取消。
    /// DPI 适配：AutoScaleMode.Dpi 让框架按显示缩放放大布局；说明放在窗内多行 Label，
    /// 不依赖标题栏显示长文本。
    /// </summary>
    private string? PromptNewAccount()
    {
        var hintText =
            "未能从已有账号中唯一识别当前登录的账号，将按“新账号”建档。\r\n" +
            "请确认退出前客户端登录的是要建档的账号，再输入账号名：";
        int msgH = 64;
        int y = 16;
        using var dlg = new Form
        {
            Text = "新建账号建档",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(460, 16 + msgH + 14 + 32 + 16 + 48 + 18),
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            Font = new Font("Microsoft YaHei UI", 9)
        };
        var hint = new Label
        {
            Left = 16, Top = y, Width = 428,
            Text = hintText, AutoSize = false, Height = msgH
        };
        y += msgH + 14;
        var tb = new TextBox { Left = 16, Top = y, Width = 428, Height = 30 };
        y += 30 + 16;
        int bw = (428 - 12) / 2;
        var ok = new Button { Text = "确定", Left = 16, Top = y, Width = bw, Height = 48, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 16 + bw + 12, Top = y, Width = bw, Height = 48, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(hint); dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Shown += (_, _) => { tb.Focus(); };
        return dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text)
            ? tb.Text.Trim()
            : null;
    }

    /// <summary>
    /// 从已有账号下拉中选择一个（识别到多个账号与 live 一致时用，用于更新其备份）。
    /// 默认不选中任何项，必须显式点选，避免回车误覆盖。返回 null 表示取消。
    /// </summary>
    private string? PromptSelectAccount(string message)
    {
        // 说明文字按行数撑高，保证相似度明细不被截断
        int lineCount = message.Split('\n').Length;
        int msgH = Math.Min(230, 20 + lineCount * 19);
        int y = 16;
        using var dlg = new Form
        {
            Text = "选择要建档的账号",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(520, y + msgH + 12 + 34 + 16 + 48 + 18),
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            Font = new Font("Microsoft YaHei UI", 9)
        };
        var msg = new Label
        {
            Left = 16, Top = y, Width = 488,
            Text = message, AutoSize = false, Height = msgH
        };
        y += msgH + 12;
        var cbo = new ComboBox
        {
            Left = 16, Top = y, Width = 488, Height = 30,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cbo.Items.AddRange(_settings.Data.Accounts.ToArray());
        cbo.SelectedIndex = -1; // 强制显式选择，不允许回车默认第一个
        y += 30 + 16;
        int bw = (488 - 12) / 2;
        var ok = new Button { Text = "确定", Left = 16, Top = y, Width = bw, Height = 48, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 16 + bw + 12, Top = y, Width = bw, Height = 48, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(msg); dlg.Controls.Add(cbo); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK && cbo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s)
            ? s.Trim()
            : null;
    }

    private void Log(string msg)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        _log.ScrollToCaret();
    }
}
