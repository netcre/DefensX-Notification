using DwhMonitor.Core;

namespace DwhMonitor.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _webhookTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "已保存时留空表示保持不变" };
    private readonly TextBox _secretTextBox = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "已保存时留空表示保持不变",
        UseSystemPasswordChar = true
    };
    private readonly TextBox _usernameTextBox = new() { Dock = DockStyle.Fill, PlaceholderText = "账号" };
    private readonly TextBox _passwordTextBox = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "新增账号必填，已有账号留空表示保持原密码",
        UseSystemPasswordChar = true
    };
    private readonly ListBox _accountList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckBox _autoStartCheckBox = new() { Text = "登录 Windows 后自动启动", AutoSize = true };
    private readonly CheckBox _autoLoginCheckBox = new() { Text = "启用账号密码自动登录", AutoSize = true };
    private readonly Label _resultLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _testButton = new() { Text = "测试钉钉", AutoSize = true };
    private readonly Button _saveButton = new() { Text = "保存并验证", AutoSize = true };
    private readonly AppSettingsStore _settingsStore;
    private readonly Func<DingTalkCredentials, CancellationToken, Task<NotificationResult>> _testSender;
    private readonly List<LoginAccountProfile> _draftAccounts;
    private string? _activeAccountId;
    private bool _testPassed;
    private bool _loadingAccount;

    public SettingsForm(
        AppSettingsStore settingsStore,
        Func<DingTalkCredentials, CancellationToken, Task<NotificationResult>> testSender)
    {
        _settingsStore = settingsStore;
        _testSender = testSender;
        _draftAccounts = settingsStore.GetLoginAccounts().ToList();
        _activeAccountId = settingsStore.ActiveAccountId ?? _draftAccounts.FirstOrDefault()?.Id;
        _testPassed = settingsStore.HasCredentials;
        _autoStartCheckBox.Checked = settingsStore.AutoStartEnabled;
        _autoLoginCheckBox.Text = CaptchaRecognizer.ReleaseAccuracyGatePassed
            ? "启用账号密码自动登录"
            : "启用账号密码自动登录（OCR 验收未达标，当前关闭）";
        _autoLoginCheckBox.Enabled = CaptchaRecognizer.ReleaseAccuracyGatePassed;
        _autoLoginCheckBox.Checked = CaptchaRecognizer.ReleaseAccuracyGatePassed && settingsStore.AutoLoginEnabled;

        Text = "盾网监控设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(920, 560);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(CreateLabel("Webhook"), 0, 0);
        layout.Controls.Add(_webhookTextBox, 1, 0);
        layout.Controls.Add(CreateLabel("加签 secret"), 0, 1);
        layout.Controls.Add(_secretTextBox, 1, 1);
        layout.Controls.Add(CreateLabel("账号列表"), 0, 2);
        layout.Controls.Add(BuildAccountEditor(), 1, 2);
        layout.Controls.Add(_autoStartCheckBox, 1, 3);
        layout.Controls.Add(_autoLoginCheckBox, 1, 4);
        layout.Controls.Add(new Label
        {
            Text = "账号和密码使用当前 Windows 用户 DPAPI 加密；保存后会立即验证活动账号。",
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 5);
        layout.Controls.Add(new Label
        {
            Text = "验证码识别失败、异地短信验证或明确凭据错误时会显示内置登录窗口。",
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 6);
        layout.Controls.Add(_resultLabel, 1, 8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(_testButton);
        layout.Controls.Add(buttons, 1, 9);
        Controls.Add(layout);

        AcceptButton = _saveButton;
        CancelButton = cancelButton;
        _webhookTextBox.TextChanged += (_, _) => OnCredentialChanged();
        _secretTextBox.TextChanged += (_, _) => OnCredentialChanged();
        _testButton.Click += async (_, _) => await TestAsync();
        _saveButton.Click += (_, _) => SaveSettings();
        RefreshAccountList();
    }

    private Control BuildAccountEditor()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0, 0, 0, 4)
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        editor.Controls.Add(_accountList, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8, 0, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.Controls.Add(CreateLabel("账号"), 0, 0);
        fields.Controls.Add(_usernameTextBox, 1, 0);
        fields.Controls.Add(CreateLabel("密码"), 0, 1);
        fields.Controls.Add(_passwordTextBox, 1, 1);
        editor.Controls.Add(fields, 1, 0);

        var accountButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 0, 0), WrapContents = false };
        var addButton = new Button { Text = "新增/更新", AutoSize = true };
        var deleteButton = new Button { Text = "删除", AutoSize = true };
        var activeButton = new Button { Text = "设为活动", AutoSize = true };
        accountButtons.Controls.Add(addButton);
        accountButtons.Controls.Add(deleteButton);
        accountButtons.Controls.Add(activeButton);
        editor.Controls.Add(accountButtons, 1, 1);
        editor.Controls.Add(new Label { Text = "活动账号：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        editor.Controls.Add(new Label { Text = "切换活动账号会清理站点状态并立即验证。", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft }, 1, 2);

        _accountList.SelectedIndexChanged += (_, _) => LoadSelectedAccount();
        addButton.Click += (_, _) => AddOrUpdateAccount();
        deleteButton.Click += (_, _) => DeleteSelectedAccount();
        activeButton.Click += (_, _) => SetSelectedAccountActive();
        return editor;
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private void RefreshAccountList()
    {
        _accountList.Items.Clear();
        foreach (var account in _draftAccounts)
        {
            var marker = account.Id == _activeAccountId ? "[活动] " : string.Empty;
            _accountList.Items.Add(marker + account.Username);
        }

        if (_draftAccounts.Count > 0)
        {
            var index = Math.Max(0, _draftAccounts.FindIndex(account => account.Id == _activeAccountId));
            _accountList.SelectedIndex = index;
        }
        else
        {
            _usernameTextBox.Clear();
            _passwordTextBox.Clear();
        }
    }

    private void LoadSelectedAccount()
    {
        if (_loadingAccount || _accountList.SelectedIndex < 0 || _accountList.SelectedIndex >= _draftAccounts.Count)
        {
            return;
        }

        _loadingAccount = true;
        try
        {
            var account = _draftAccounts[_accountList.SelectedIndex];
            _usernameTextBox.Text = account.Username;
            _passwordTextBox.Clear();
        }
        finally
        {
            _loadingAccount = false;
        }
    }

    private void AddOrUpdateAccount()
    {
        var username = _usernameTextBox.Text.Trim();
        var existingIndex = _draftAccounts.FindIndex(account =>
            string.Equals(account.Username, username, StringComparison.OrdinalIgnoreCase));
        var existing = existingIndex >= 0 ? _draftAccounts[existingIndex] : null;
        var password = string.IsNullOrWhiteSpace(_passwordTextBox.Text)
            ? existing?.Password
            : _passwordTextBox.Text;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowResult("请输入账号和密码。", false);
            return;
        }

        var account = new LoginAccountProfile(existing?.Id ?? Guid.NewGuid().ToString("N"), username, password, null);
        if (existing is null)
        {
            _draftAccounts.Add(account);
            _activeAccountId ??= account.Id;
        }
        else
        {
            _draftAccounts[existingIndex] = account;
        }

        _testPassed = false;
        _passwordTextBox.Clear();
        RefreshAccountList();
        ShowResult("账号已加入待保存列表，请点击“保存并验证”。", true);
    }

    private void DeleteSelectedAccount()
    {
        if (_accountList.SelectedIndex < 0 || _accountList.SelectedIndex >= _draftAccounts.Count)
        {
            return;
        }

        var removed = _draftAccounts[_accountList.SelectedIndex];
        _draftAccounts.RemoveAt(_accountList.SelectedIndex);
        if (_activeAccountId == removed.Id)
        {
            _activeAccountId = _draftAccounts.FirstOrDefault()?.Id;
        }

        _testPassed = false;
        RefreshAccountList();
    }

    private void SetSelectedAccountActive()
    {
        if (_accountList.SelectedIndex < 0 || _accountList.SelectedIndex >= _draftAccounts.Count)
        {
            return;
        }

        _activeAccountId = _draftAccounts[_accountList.SelectedIndex].Id;
        _testPassed = false;
        RefreshAccountList();
        ShowResult("活动账号已变更，请保存并验证。", true);
    }

    private void OnCredentialChanged()
    {
        if (_webhookTextBox.TextLength > 0 || _secretTextBox.TextLength > 0)
        {
            _testPassed = false;
            ShowResult("钉钉配置已修改，请重新测试。", false);
        }
    }

    private DingTalkCredentials? ResolveCredentials()
    {
        var existing = _settingsStore.GetCredentials();
        var webhook = string.IsNullOrWhiteSpace(_webhookTextBox.Text) ? existing?.Webhook : _webhookTextBox.Text.Trim();
        var secret = string.IsNullOrWhiteSpace(_secretTextBox.Text) ? existing?.Secret : _secretTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(webhook) || string.IsNullOrWhiteSpace(secret)
            ? null
            : new DingTalkCredentials(webhook, secret);
    }

    private async Task TestAsync()
    {
        var credentials = ResolveCredentials();
        if (credentials is null || !IsValidWebhook(credentials.Webhook))
        {
            ShowResult("请输入有效的钉钉加签 Webhook 和 secret。", false);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _testSender(credentials, CancellationToken.None);
            _testPassed = result.Success;
            ShowResult(result.Success ? "钉钉测试消息发送成功。" : result.Error ?? "钉钉测试失败。", result.Success);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SaveSettings()
    {
        var credentials = ResolveCredentials();
        if (credentials is null || !IsValidWebhook(credentials.Webhook))
        {
            ShowResult("请输入有效的钉钉加签 Webhook 和 secret。", false);
            return;
        }

        if (!_testPassed)
        {
            ShowResult("保存前必须先通过钉钉测试。", false);
            return;
        }

        if (_autoLoginCheckBox.Checked && _draftAccounts.Count == 0)
        {
            ShowResult("启用自动登录前至少添加一个账号。", false);
            return;
        }

        _settingsStore.SaveCredentials(credentials.Webhook, credentials.Secret, _autoStartCheckBox.Checked);
        _settingsStore.SaveLoginSettings(
            _draftAccounts,
            _activeAccountId,
            CaptchaRecognizer.ReleaseAccuracyGatePassed && _autoLoginCheckBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool IsValidWebhook(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.EndsWith("dingtalk.com", StringComparison.OrdinalIgnoreCase) &&
        uri.Query.Contains("access_token=", StringComparison.OrdinalIgnoreCase);

    private void SetBusy(bool busy)
    {
        _testButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ShowResult(string message, bool success)
    {
        _resultLabel.Text = message;
        _resultLabel.ForeColor = success ? Color.ForestGreen : Color.Firebrick;
    }
}
