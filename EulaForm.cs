namespace TraeSwitch;

/// <summary>
/// 首次启动的最终用户许可协议弹窗。点击「同意并继续」→ DialogResult.OK 并由调用方
/// 写入 settings.json（EulaAccepted=true），下次不再弹出；「不同意并退出」→ Cancel。
/// </summary>
public sealed class EulaForm : Form
{
    private readonly CheckBox _chkAgree = new();
    private readonly Button _btnOk = new();

    private const string EulaText =
"""
TRAETOOLS 最终用户许可协议（EULA）

欢迎使用本软件。在使用本软件前，请仔细阅读以下条款。
点击「同意并继续」即表示您接受本协议全部内容；如不同意，请立即关闭并删除本软件。

1. 软件性质
   本软件为作者个人技术学习成果，以开源形式（GPL-3.0）发布，按"现状"提供，
   不包含任何形式的明示或默示担保。

2. 账号归属限制
   您只能将本软件用于管理您本人合法拥有并有权使用的 Trae 账号。
   您不得使用本软件从事批量注册、账号交易、积分倒卖、破坏平台正常运营
   或任何违反中华人民共和国法律法规及 Trae 平台服务条款的行为。

3. 数据安全责任
   本软件可能在您的本地设备上存储登录凭证或会话信息。
   您有责任确保这些文件不被上传至公共网络、云盘或分享给任何第三方。
   因您未妥善保管本地数据导致的账号被盗、信息泄露或其他损失，作者不承担责任。

3.5 本地备份特别提示
   本软件创建的 vault 目录包含您的账号登录态文件，等同于您的账号密码。
   您确认：
   - 这些文件仅存储于您本人控制的本地设备，不上传至任何网络位置；
   - 您不会将其用于非本人账号，或分享给他人使用；
   - 您了解泄露此类文件可能导致严重的账号安全风险。

4. 平台关系声明
   本软件与 Trae 及其运营方无任何关联。
   如官方推出同类功能或要求停止使用，您应立即停用本软件。

5. 禁止逆向与滥用
   您不得对本软件进行反编译、破解或二次开发后用于违反平台规则的目的。
   您不得移除、修改或绕过本软件中的任何使用限制或安全提示。

6. 责任免除
   在法律允许的最大范围内，作者不对因使用或无法使用本软件而产生的任何
   直接、间接、附带或惩罚性损害承担责任，包括但不限于账号限制、积分调整、
   数据丢失等。

7. 协议更新
   作者保留随时修改本协议的权利，修改后的协议将在软件更新或仓库中公布。
""";

    public EulaForm()
    {
        Text = "最终用户许可协议";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 520);
        ClientSize = new Size(620, 600);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        var title = new Label
        {
            Text = "TRAETOOLS 最终用户许可协议",
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };

        var body = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = EulaText,
            BackColor = Color.FromArgb(250, 250, 250),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            WordWrap = false
        };

        var cbPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _chkAgree.Text = "我已阅读并同意上述协议，且确认本软件仅用于管理本人拥有的账号";
        _chkAgree.AutoSize = true;
        _chkAgree.CheckedChanged += (_, _) => _btnOk.Enabled = _chkAgree.Checked;
        cbPanel.Controls.Add(_chkAgree);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 12, 8)
        };
        _btnOk.Text = "同意并继续";
        _btnOk.Enabled = false;
        _btnOk.Width = 130;
        _btnOk.Click += (_, _) => DialogResult = DialogResult.OK;
        var btnNo = new Button { Text = "不同意并退出", Width = 130 };
        btnNo.Click += (_, _) => DialogResult = DialogResult.Cancel;
        btnPanel.Controls.Add(_btnOk);
        btnPanel.Controls.Add(btnNo);

        Controls.Add(body);
        Controls.Add(cbPanel);
        Controls.Add(btnPanel);
        Controls.Add(title);
        AcceptButton = _btnOk;
        CancelButton = btnNo;
    }
}