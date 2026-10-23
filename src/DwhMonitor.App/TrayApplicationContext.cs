using System.Diagnostics;
using DwhMonitor.Core;
using Microsoft.Web.WebView2.Core;

namespace DwhMonitor.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly AppSettingsStore _settingsStore;
    private readonly FileMonitorLogger _logger;
    private readonly StatusForm _statusForm = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _autoStartItem;
    private HttpClient? _apiHttpClient;
    private HttpClient? _dingTalkHttpClient;
    private SessionWebViewForm? _sessionForm;
    private MonitorEngine? _engine;
    private Task? _monitorTask;
    private readonly SemaphoreSlim _autoLoginLock = new(1, 1);
    private CaptchaRecognizer? _captchaRecognizer;
    private DateTimeOffset? _nextAutoLoginUtc;
    private int _autoLoginNetworkFailures;
    private bool _initialized;
    private bool _exiting;

    public TrayApplicationContext()
    {
        AppPaths.EnsureCreated();
        _logger = new FileMonitorLogger();
        if (AppPaths.LastMigrationResult is { } migration)
        {
            if (migration.CopiedFiles > 0)
            {
                _logger.Info($"已将 {migration.CopiedFiles} 个数据文件迁移到软件运行目录");
            }

            foreach (var error in migration.Errors)
            {
                _logger.Error($"运行目录数据迁移失败：{error}");
            }
        }

        _settingsStore = new AppSettingsStore();

        var menu = new ContextMenuStrip();
        menu.Items.Add("查看状态", null, (_, _) => ShowStatus());
        menu.Items.Add("重新登录", null, async (_, _) => await ShowLoginAsync());
        menu.Items.Add("设置", null, async (_, _) => await ShowSettingsAsync());
        menu.Items.Add("测试钉钉", null, async (_, _) => await TestDingTalkAsync());
        menu.Items.Add("打开数据目录", null, (_, _) => OpenDataDirectory());
        _autoStartItem = new ToolStripMenuItem("登录 Windows 后自动启动")
        {
            Checked = AutostartManager.IsEnabled()
        };
        _autoStartItem.Click += (_, _) => ToggleAutostart();
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, async (_, _) => await ExitAsync());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(Color.Goldenrod),
            Text = "盾网监控：正在启动",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatus();
        Application.Idle += OnFirstIdle;
    }

    private async void OnFirstIdle(object? sender, EventArgs eventArgs)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Application.Idle -= OnFirstIdle;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _apiHttpClient = new HttpClient
            {
                BaseAddress = new Uri(MonitorDefaults.BaseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
            _apiHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DwhMonitor/1.0");
            _dingTalkHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var apiClient = new DwhApiClient(_apiHttpClient);
            _sessionForm = new SessionWebViewForm(apiClient, _logger);
            await _sessionForm.InitializeAsync();
            _sessionForm.SessionValidated += (_, _) => _ = HandleSessionValidatedAsync();
            _captchaRecognizer = CreateCaptchaRecognizer();

            var notifier = new DingTalkNotifier(
                _dingTalkHttpClient,
                () => _settingsStore.GetCredentials());
            var store = new SqliteMonitorStore(AppPaths.DatabasePath);
            _engine = new MonitorEngine(apiClient, _sessionForm, store, notifier, logger: _logger);
            _engine.StatusChanged += OnStatusChanged;
            await _engine.InitializeAsync(_shutdown.Token);

            var firstRun = !_settingsStore.HasCredentials;
            if (firstRun)
            {
                await ShowSettingsAsync();
            }

            if (!_settingsStore.HasCredentials)
            {
                SetTrayState(Color.Goldenrod, "盾网监控：等待钉钉配置");
                return;
            }

            ApplyAutostartSetting();
            StartMonitorLoop();
            if (CaptchaRecognizer.ReleaseAccuracyGatePassed &&
                _settingsStore.AutoLoginEnabled &&
                _settingsStore.GetActiveLoginAccount() is not null)
            {
                _ = AttemptAutoLoginIfDueAsync(force: true);
            }
            else if (firstRun || string.IsNullOrWhiteSpace(await _sessionForm.GetCookieHeaderAsync(_shutdown.Token)))
            {
                await _sessionForm.ShowLoginAsync();
            }
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _logger.Error("未检测到 Microsoft Edge WebView2 Runtime", exception);
            SetTrayState(Color.Firebrick, "盾网监控：缺少 WebView2 Runtime");
            MessageBox.Show(
                "未检测到 Microsoft Edge WebView2 Runtime。请安装 WebView2 Evergreen Runtime 后重新启动程序。\n\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "盾网监控",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception exception)
        {
            _logger.Error("程序初始化失败", exception);
            SetTrayState(Color.Firebrick, "盾网监控：初始化失败");
            MessageBox.Show(
                $"程序初始化失败：{exception.Message}\n详细信息已写入日志。",
                "盾网监控",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartMonitorLoop()
    {
        if (_monitorTask is not null || _engine is null)
        {
            return;
        }

        _monitorTask = Task.Run(
            () => _engine.RunAsync(
                TimeSpan.FromSeconds(MonitorDefaults.PollIntervalSeconds),
                _shutdown.Token),
            _shutdown.Token);
    }

    private void TriggerPoll()
    {
        if (_engine is null || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _ = Task.Run(() => _engine.PollOnceAsync(_shutdown.Token), _shutdown.Token);
    }

    private void OnStatusChanged(object? sender, MonitorRuntimeStatus status)
    {
        if (_sessionForm is null || _sessionForm.IsDisposed)
        {
            return;
        }

        if (_sessionForm.InvokeRequired)
        {
            _sessionForm.BeginInvoke(() => UpdateUi(status));
        }
        else
        {
            UpdateUi(status);
        }
    }

    private void UpdateUi(MonitorRuntimeStatus status)
    {
        _statusForm.UpdateStatus(status);
        if (status.ActiveAlerts.Contains(AlertKind.SessionExpired))
        {
            _sessionForm?.MarkSessionInvalid(status.LastError);
            _ = AttemptAutoLoginIfDueAsync(force: false);
        }

        if (status.ActiveAlerts.Count > 0)
        {
            SetTrayState(Color.Firebrick, $"盾网监控：{status.ActiveAlerts.Count} 个告警");
        }
        else if (!status.SessionValid || status.ConsecutiveApiFailures > 0)
        {
            SetTrayState(Color.Goldenrod, "盾网监控：等待恢复");
        }
        else
        {
            SetTrayState(Color.ForestGreen, "盾网监控：运行正常");
        }
    }

    private void ShowStatus()
    {
        if (_engine is not null)
        {
            _statusForm.UpdateStatus(_engine.Status);
        }

        _statusForm.Show();
        _statusForm.WindowState = FormWindowState.Normal;
        _statusForm.Activate();
    }

    private async Task ShowLoginAsync()
    {
        if (_sessionForm is null)
        {
            MessageBox.Show("登录组件尚未初始化。", "盾网监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (CaptchaRecognizer.ReleaseAccuracyGatePassed &&
                _settingsStore.AutoLoginEnabled &&
                _settingsStore.GetActiveLoginAccount() is not null &&
                _captchaRecognizer is not null)
            {
                _nextAutoLoginUtc = null;
                _autoLoginNetworkFailures = 0;
                await AttemptAutoLoginIfDueAsync(force: true);
                return;
            }

            await _sessionForm.ShowLoginAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("打开盾网登录页失败", exception);
            MessageBox.Show($"打开登录页失败：{exception.Message}", "盾网监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (_dingTalkHttpClient is null)
        {
            return;
        }

        var previousAccountId = _settingsStore.ActiveAccountId;
        using var form = new SettingsForm(_settingsStore, SendTestAsync);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        ApplyAutostartSetting();
        StartMonitorLoop();
        var activeChanged = !string.Equals(previousAccountId, _settingsStore.ActiveAccountId, StringComparison.Ordinal);
        if (_sessionForm is not null && activeChanged)
        {
            await _sessionForm.ClearSiteDataAsync(_shutdown.Token);
            _nextAutoLoginUtc = null;
            _autoLoginNetworkFailures = 0;
        }

        if (CaptchaRecognizer.ReleaseAccuracyGatePassed &&
            _settingsStore.AutoLoginEnabled &&
            _settingsStore.GetActiveLoginAccount() is not null)
        {
            await AttemptAutoLoginIfDueAsync(force: true);
        }
        else if (_sessionForm is not null &&
                 (activeChanged || string.IsNullOrWhiteSpace(await _sessionForm.GetCookieHeaderAsync(_shutdown.Token))))
        {
            await _sessionForm.ShowLoginAsync();
        }
    }

    private CaptchaRecognizer? CreateCaptchaRecognizer()
    {
        try
        {
            return new CaptchaRecognizer(AppContext.BaseDirectory);
        }
        catch (Exception exception)
        {
            _logger.Error("本地验证码识别器初始化失败，自动登录保持关闭", exception);
            return null;
        }
    }

    private async Task HandleSessionValidatedAsync()
    {
        if (_sessionForm is null || _shutdown.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var session = await _sessionForm.TryReadCurrentSessionAsync(_shutdown.Token);
            if (session?.IsValid != true)
            {
                return;
            }

            var account = _settingsStore.GetActiveLoginAccount();
            if (account is not null && session?.Account is not null &&
                !string.Equals(
                    NormalizeAccount(account.Username),
                    NormalizeAccount(session.Account),
                    StringComparison.Ordinal))
            {
                await _sessionForm.ClearSiteDataAsync(_shutdown.Token);
                await _sessionForm.ShowForUserActionAsync(
                    $"当前页面账号 {session.Account} 与活动账号不一致，请在设置中切换活动账号。");
                return;
            }

            if (account is not null)
            {
                _settingsStore.MarkLoginAccountVerified(account.Id, DateTimeOffset.UtcNow);
                if (_engine is not null)
                {
                    await _engine.SetAutoLoginFailureAsync(false, account.Username, null, _shutdown.Token);
                }
            }

            _nextAutoLoginUtc = null;
            _autoLoginNetworkFailures = 0;
            TriggerPoll();
        }
        catch (Exception exception)
        {
            _logger.Error("登录成功后的账号核对失败", exception);
        }
    }

    private async Task AttemptAutoLoginIfDueAsync(bool force)
    {
        if (!CaptchaRecognizer.ReleaseAccuracyGatePassed ||
            _sessionForm is null || _engine is null || !_settingsStore.AutoLoginEnabled ||
            _settingsStore.GetActiveLoginAccount() is null || _shutdown.IsCancellationRequested)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && _nextAutoLoginUtc.HasValue && now < _nextAutoLoginUtc.Value)
        {
            return;
        }

        if (!await _autoLoginLock.WaitAsync(0, _shutdown.Token))
        {
            return;
        }

        try
        {
            var account = _settingsStore.GetActiveLoginAccount();
            if (account is null)
            {
                return;
            }

            var session = await _sessionForm.TryReadCurrentSessionAsync(_shutdown.Token);
            if (session?.IsValid == true &&
                (session.Account is null || string.Equals(
                    NormalizeAccount(account.Username),
                    NormalizeAccount(session.Account),
                    StringComparison.Ordinal)))
            {
                await HandleSessionValidatedAsync();
                return;
            }

            if (_captchaRecognizer is null)
            {
                await HandleAutoLoginFailureAsync(account, new AutoLoginResult(
                    false,
                    AutoLoginFailureKind.PageContractChanged,
                    "本地验证码识别器未就绪",
                    true));
                return;
            }

            await _sessionForm.ClearSiteDataAsync(_shutdown.Token);
            _logger.Info("自动登录前已清理失效 Token 和浏览器会话状态");
            var result = await _sessionForm.TryAutoLoginAsync(account, _captchaRecognizer, _shutdown.Token);
            if (result.Success)
            {
                _logger.Info(
                    $"自动登录成功，提交 {result.SubmittedCaptchaAttempts}/5 次，读取 {result.UniqueCaptchaImages} 张不同验证码，刷新 {result.CaptchaRefreshes} 次");
                await HandleSessionValidatedAsync();
                return;
            }

            await HandleAutoLoginFailureAsync(account, result);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("自动登录尝试失败", exception);
            var account = _settingsStore.GetActiveLoginAccount();
            if (account is not null)
            {
                await HandleAutoLoginFailureAsync(account, new AutoLoginResult(
                    false,
                    AutoLoginFailureKind.NetworkUnavailable,
                    exception.Message));
            }
        }
        finally
        {
            _autoLoginLock.Release();
        }
    }

    private async Task HandleAutoLoginFailureAsync(LoginAccountProfile account, AutoLoginResult result)
    {
        var reason = result.FailureKind switch
        {
            AutoLoginFailureKind.CredentialRejected => "账号或密码错误",
            AutoLoginFailureKind.CaptchaExhausted => "验证码尝试耗尽",
            AutoLoginFailureKind.NetworkUnavailable => "页面或网络故障",
            AutoLoginFailureKind.SmsRequired => "需要短信验证",
            AutoLoginFailureKind.PageContractChanged => "登录页面结构变化",
            AutoLoginFailureKind.UnexpectedResponse => "登录接口返回异常",
            _ => "自动登录失败"
        };
        var metrics = result.UniqueCaptchaImages > 0 || result.SubmittedCaptchaAttempts > 0
            ? $"提交 {result.SubmittedCaptchaAttempts}/5 次，读取 {result.UniqueCaptchaImages} 张不同验证码，刷新 {result.CaptchaRefreshes} 次"
            : null;
        var detail = result.Error ?? reason;
        var error = metrics is null ? $"{reason}：{detail}" : $"{reason}（{metrics}）：{detail}";
        if (result.FailureKind == AutoLoginFailureKind.NetworkUnavailable)
        {
            _autoLoginNetworkFailures++;
            var delay = _autoLoginNetworkFailures switch
            {
                1 => TimeSpan.FromSeconds(30),
                2 => TimeSpan.FromMinutes(1),
                3 => TimeSpan.FromMinutes(2),
                _ => TimeSpan.FromMinutes(5)
            };
            _nextAutoLoginUtc = DateTimeOffset.UtcNow + delay;
            if (_autoLoginNetworkFailures < 4)
            {
                _logger.Error($"自动登录网络失败，{delay.TotalSeconds:0} 秒后重试：{error}");
                return;
            }
        }
        else
        {
            _autoLoginNetworkFailures = 0;
            _nextAutoLoginUtc = result.RequiresUserAction ? DateTimeOffset.MaxValue : null;
        }

        if (_engine is not null)
        {
            await _engine.SetAutoLoginFailureAsync(true, account.Username, error, _shutdown.Token);
        }

        if (result.RequiresUserAction && _sessionForm is not null)
        {
            await _sessionForm.ShowForUserActionAsync(error);
        }
    }

    private static string NormalizeAccount(string account) =>
        account.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

    private async Task<NotificationResult> SendTestAsync(
        DingTalkCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (_dingTalkHttpClient is null)
        {
            return NotificationResult.Failed("钉钉客户端尚未初始化");
        }

        var notifier = new DingTalkNotifier(_dingTalkHttpClient, () => credentials);
        return await notifier.SendAsync(new NotificationMessage(
            "[盾网监控] 配置测试",
            $"### [盾网监控] 配置测试\n\n- 结果：钉钉机器人连接成功\n\n- 时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"), cancellationToken);
    }

    private async Task TestDingTalkAsync()
    {
        var credentials = _settingsStore.GetCredentials();
        if (credentials is null)
        {
            MessageBox.Show("请先配置钉钉机器人。", "盾网监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = await SendTestAsync(credentials, CancellationToken.None);
        MessageBox.Show(
            result.Success ? "钉钉测试消息发送成功。" : result.Error ?? "钉钉测试失败。",
            "盾网监控",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    private void ToggleAutostart()
    {
        var enabled = !AutostartManager.IsEnabled();
        try
        {
            AutostartManager.SetEnabled(enabled);
            _settingsStore.SetAutoStart(enabled);
            _autoStartItem.Checked = enabled;
        }
        catch (Exception exception)
        {
            _logger.Error("更新开机自启失败", exception);
            MessageBox.Show($"更新开机自启失败：{exception.Message}", "盾网监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyAutostartSetting()
    {
        AutostartManager.SetEnabled(_settingsStore.AutoStartEnabled);
        _autoStartItem.Checked = _settingsStore.AutoStartEnabled;
    }

    private static void OpenDataDirectory()
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.RootDirectory) { UseShellExecute = true });
    }

    private void SetTrayState(Color color, string text)
    {
        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = TrayIconFactory.Create(color);
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
        oldIcon?.Dispose();
    }

    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _shutdown.Cancel();
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _captchaRecognizer?.Dispose();
        _autoLoginLock.Dispose();
        _sessionForm?.Shutdown();
        _statusForm.Shutdown();
        _apiHttpClient?.Dispose();
        _dingTalkHttpClient?.Dispose();
        _shutdown.Dispose();
        ExitThread();
    }
}
