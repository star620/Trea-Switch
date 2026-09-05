using TraeSwitch.Services;

namespace TraeSwitch;

public class MainForm : Form
{
    private readonly SettingsStore _settings;
    private readonly ListBox _accounts;
    private readonly TextBox _log;
    private readonly VaultService _vault;

    public MainForm(SettingsStore settings)
    {
        _settings = settings;
        var root = _settings.Data.RootDir;
        var vaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraeSwitch", "vault");
        _vault = new VaultService(root, vaultRoot);

        Text = "TraeSwitch 账号冷切换助手";
        ClientSize = new Size(900, 600);
        BackColor = AppStyle.ContentBack;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.RowCount = 2;
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _accounts = new ListBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10) };
        _accounts.DataSource = _settings.Data.Accounts;
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
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = $"用户数据目录：{_settings.Data.RootDir}\n客户端：{_settings.Data.ClientExe}",
            Font = new Font("Microsoft YaHei UI", 9),
            ForeColor = AppStyle.TextDark
        };
    }

    private Control BuildActions()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false
        };
        row.Controls.Add(MakeBtn("建档（备份当前账号）", async () => await DoBackupAsync()));
        row.Controls.Add(MakeBtn("切换到选中账号", async () => await DoSwitchAsync()));
        row.Controls.Add(MakeBtn("校验选中账号", async () => await DoVerifyAsync()));
        row.Controls.Add(MakeBtn("删除选中账号", DoDelete));
        row.Controls.Add(MakeBtn("刷新列表", RefreshAccounts));
        return row;
    }

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

    private void RefreshAccounts()
    {
        var sel = _accounts.SelectedItem as string;
        _accounts.DataSource = null;
        _accounts.DataSource = _settings.Data.Accounts;
        if (sel != null) _accounts.SelectedItem = sel;
    }

    private async Task DoBackupAsync()
    {
        var client = new ClientController(_settings.Data.ProcessName, _settings.Data.ClientExe);
        if (client.IsRunning())
        {
            Log("无法建档：TRAE SOLO CN 正在运行，登录态文件被占用。请先完全退出客户端（托盘也退出），再点「建档」。");
            return;
        }
        var name = PromptAccount("建档：输入当前登录的账号名");
        if (name == null) return;
        try
        {
            if (!_settings.Data.Accounts.Contains(name)) _settings.Data.Accounts.Add(name);
            await _vault.BackupAsync(name, _settings.Data.Fingerprint);
            _settings.Save();
            Log($"已备份账号 {name}（载体条目 {_settings.Data.Fingerprint.Count} 个）。提示：请确认刚才退出前客户端登录的确实是该账号。");
        }
        catch (Exception ex)
        {
            Log("建档失败：" + ex.Message);
        }
        RefreshAccounts();
    }

    private async Task DoSwitchAsync()
    {
        var name = _accounts.SelectedItem as string;
        if (name == null) { Log("请先选中目标账号。"); return; }
        if (_settings.Data.Fingerprint.Count == 0)
        {
            Log("尚未配置载体指纹：请先运行 Phase 0 定位脚本，把变化的相对路径填到 settings.json 的 Fingerprint 后重试。");
            return;
        }
        var client = new ClientController(_settings.Data.ProcessName, _settings.Data.ClientExe);
        var sw = new SwitcherService(_settings.Data.RootDir, _vault, client);
        try
        {
            Log($"开始切换到 {name}…");
            await sw.SwitchToAsync(name, _settings.Data.Fingerprint);
            Log($"已切换并启动客户端。请确认界面登录的是 {name}。");
        }
        catch (Exception ex)
        {
            Log("切换失败：" + ex.Message);
        }
    }

    private async Task DoVerifyAsync()
    {
        var name = _accounts.SelectedItem as string;
        if (name == null) { Log("请先选中账号。"); return; }
        var client = new ClientController(_settings.Data.ProcessName, _settings.Data.ClientExe);
        if (client.IsRunning())
        {
            Log("无法校验：TRAE SOLO CN 正在运行，登录态文件被占用。请先完全退出客户端再点「校验」。");
            return;
        }
        try
        {
            var ok = await _vault.VerifyAsync(name, _settings.Data.Fingerprint);
            Log(ok
                ? $"账号 {name} vault 与 live 一致。"
                : $"账号 {name} vault 校验失败：客户端可能改写了登录态，请重新登录该账号后点「建档」更新备份。");
        }
        catch (Exception ex)
        {
            Log("校验失败：" + ex.Message);
        }
    }

    private void DoDelete()
    {
        var name = _accounts.SelectedItem as string;
        if (name == null) return;
        _settings.Data.Accounts.Remove(name);
        _settings.Save();
        RefreshAccounts();
        Log($"已从列表移除 {name}（vault 目录建议手动删除以清空登录态字节）。");
    }

    private string? PromptAccount(string title)
    {
        using var dlg = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(320, 110), StartPosition = FormStartPosition.CenterParent
        };
        var tb = new TextBox { Left = 12, Top = 16, Width = 296 };
        var ok = new Button { Text = "确定", Left = 12, Top = 56, Width = 140, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 168, Top = 56, Width = 140, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
        return dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text) ? tb.Text.Trim() : null;
    }

    private void Log(string msg) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
}
