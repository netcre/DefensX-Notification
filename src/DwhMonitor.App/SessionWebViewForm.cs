using System.Text.Json;
using DwhMonitor.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DwhMonitor.App;

internal sealed class SessionWebViewForm : Form, ICookieProvider
{
    private static readonly TimeSpan CookieReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LoginResponseTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan CaptchaAutoRefreshWait = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan CaptchaRefreshTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "正在准备登录页面...",
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly System.Windows.Forms.Timer _validationTimer = new() { Interval = 2500 };
    private readonly IDwhApiClient _apiClient;
    private readonly IMonitorLogger _logger;
    private TaskCompletionSource<LoginResponse>? _loginResponseSource;
    private bool _initialized;
    private bool _checkingSession;
    private bool _allowClose;

    public SessionWebViewForm(IDwhApiClient apiClient, IMonitorLogger logger)
    {
        _apiClient = apiClient;
        _logger = logger;
        Text = "盾网账号登录";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 640);
        ClientSize = new Size(1180, 780);
        ShowInTaskbar = true;

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            ColumnCount = 2,
            Padding = new Padding(10, 5, 10, 5)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var reloadButton = new Button { Text = "重新加载", Dock = DockStyle.Fill };
        reloadButton.Click += (_, _) => _webView.CoreWebView2?.Reload();
        footer.Controls.Add(_statusLabel, 0, 0);
        footer.Controls.Add(reloadButton, 1, 0);
        Controls.Add(_webView);
        Controls.Add(footer);

        _validationTimer.Tick += async (_, _) => await ValidateSessionAsync();
        FormClosing += OnFormClosing;
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                _validationTimer.Start();
            }
            else
            {
                _validationTimer.Stop();
            }
        };
    }

    public event EventHandler? SessionValidated;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _ = Handle;
        _webView.CreateControl();
        var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
        if (string.IsNullOrWhiteSpace(runtimeVersion))
        {
            throw new WebView2RuntimeNotFoundException();
        }

        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.WebViewDirectory);
        await _webView.EnsureCoreWebView2Async(environment);
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        _webView.CoreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;
        _initialized = true;
        _logger.Info($"WebView2 会话初始化完成，运行时版本 {runtimeVersion}");
    }

    public async Task ShowLoginAsync()
    {
        await InitializeAsync();
        _statusLabel.Text = "请在页面中完成盾网登录，程序会自动验证会话。";
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        NavigateToLogin();
        _validationTimer.Start();
    }

    public async Task ShowForUserActionAsync(string message)
    {
        await InitializeAsync();
        _statusLabel.Text = message;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _validationTimer.Start();
    }

    public async Task ClearSiteDataAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteScriptAsync("try { localStorage.clear(); sessionStorage.clear(); } catch {} true;", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
        cancellationToken.ThrowIfCancellationRequested();
        _webView.CoreWebView2.Navigate("about:blank");
        await Task.Delay(250, cancellationToken);
        _logger.Info("已清理盾网站点浏览器状态");
    }

    public async Task<AutoLoginResult> TryAutoLoginAsync(
        LoginAccountProfile account,
        CaptchaRecognizer recognizer,
        CancellationToken cancellationToken,
        int maximumCaptchaAttempts = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCaptchaAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCaptchaAttempts, 5);
        await InitializeAsync();
        _validationTimer.Stop();
        Hide();
        NavigateToLogin();

        var contract = await WaitForLoginContractAsync(cancellationToken);
        if (!contract.Ready)
        {
            return new AutoLoginResult(false, AutoLoginFailureKind.PageContractChanged, contract.Error, true);
        }

        return await CaptchaRetryCoordinator.RunAsync(
            maximumCaptchaAttempts,
            ReadCaptchaFrameAsync,
            RefreshAndReadCaptchaFrameAsync,
            recognizer.Recognize,
            (captcha, image, token) => SubmitCaptchaAsync(account, captcha, image, token),
            cancellationToken);
    }

    public void MarkSessionInvalid(string? message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => MarkSessionInvalid(message));
            return;
        }

        _statusLabel.Text = message ?? "盾网登录会话已失效，请重新登录。";
    }

    public Task<string?> GetCookieHeaderAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return Task.FromResult<string?>(null);
        }

        if (!InvokeRequired)
        {
            return GetCookieHeaderCoreAsync(cancellationToken).WaitAsync(CookieReadTimeout, cancellationToken);
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(async () =>
        {
            try
            {
                completion.TrySetResult(await GetCookieHeaderCoreAsync(cancellationToken));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task.WaitAsync(CookieReadTimeout, cancellationToken);
    }

    public async Task<SessionUserInfo?> TryReadCurrentSessionAsync(CancellationToken cancellationToken)
    {
        var cookieHeader = await GetCookieHeaderAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        try
        {
            return await _apiClient.GetSessionInfoAsync(cookieHeader, cancellationToken);
        }
        catch (SessionExpiredException)
        {
            return null;
        }
    }

    public void Shutdown()
    {
        _allowClose = true;
        _validationTimer.Stop();
        if (_initialized)
        {
            _webView.CoreWebView2.WebResourceResponseReceived -= OnWebResourceResponseReceived;
        }
        Close();
    }

    private void NavigateToLogin() => _webView.CoreWebView2.Navigate($"{MonitorDefaults.BaseUrl}/#/login");

    private async Task<LoginContractState> WaitForLoginContractAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        string? lastContractError = null;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await ExecuteJsonAsync<LoginPageSnapshot>("""
                (() => {
                    const matches = (selector, predicate) =>
                        [...document.querySelectorAll(selector)].filter(predicate);
                    const visible = node => {
                        const style = window.getComputedStyle(node);
                        return style.display !== 'none' && style.visibility !== 'hidden' &&
                               node.getClientRects().length > 0;
                    };
                    const tabs = matches('[role="tab"]', node =>
                        (node.textContent || '').includes('账号密码'));
                    if (tabs.length === 1 && tabs[0].getAttribute('aria-selected') !== 'true') tabs[0].click();
                    const inputs = placeholder => matches('input', node =>
                        node.getAttribute('placeholder') === placeholder);
                    const users = inputs('请输入用户名');
                    const passwords = inputs('请输入密码');
                    const captchas = inputs('请输入验证码');
                    const images = matches('img[alt="点击刷新验证码"]', () => true);
                    const buttons = matches('button', node =>
                        (node.textContent || '').includes('登录管理平台'));
                    const counts = [tabs.length, users.length, passwords.length, captchas.length,
                                    images.length, buttons.length];
                    const ready = counts.every(count => count === 1);
                    const tokenText = text =>
                        text.includes('Token已经过期') || text.includes('Token 已经过期') ||
                        text.includes('Token已经逾期') || text.includes('Token 已经逾期') ||
                        text.includes('该账号Token已经过期');
                    const dialogs = [...document.querySelectorAll('.el-message-box__wrapper, [role="dialog"], [role="alertdialog"]')]
                        .filter(visible);
                    const expiredDialogs = dialogs.filter(dialog => tokenText(dialog.innerText || ''));
                    const reloginButtons = expiredDialogs.flatMap(dialog =>
                        [...dialog.querySelectorAll('button')].filter(button =>
                            (button.textContent || '').trim() === '重新登录'));
                    return {
                        ready,
                        tokenExpiredDialogVisible: expiredDialogs.length > 0,
                        reloginButtonCount: reloginButtons.length,
                        contractError: ready ? null :
                            `登录控件唯一性检查失败（账号标签/账号/密码/验证码/图片/按钮：${counts.join('/')}）`
                    };
                })()
            """, cancellationToken);
            if (snapshot is null)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            lastContractError = snapshot.ContractError;
            var recovery = LoginPageRecovery.Decide(snapshot);
            if (recovery.Action == LoginPageRecoveryAction.ContractChanged)
            {
                return new LoginContractState(false, recovery.Error);
            }

            if (recovery.Action == LoginPageRecoveryAction.ClickExpiredTokenRelogin)
            {
                var clicked = await ExecuteJsonAsync<bool>("""
                    (() => {
                        const visible = node => {
                            const style = window.getComputedStyle(node);
                            return style.display !== 'none' && style.visibility !== 'hidden' &&
                                   node.getClientRects().length > 0;
                        };
                        const tokenText = text =>
                            text.includes('Token已经过期') || text.includes('Token 已经过期') ||
                            text.includes('Token已经逾期') || text.includes('Token 已经逾期') ||
                            text.includes('该账号Token已经过期');
                        const dialogs = [...document.querySelectorAll('.el-message-box__wrapper, [role="dialog"], [role="alertdialog"]')]
                            .filter(dialog => visible(dialog) && tokenText(dialog.innerText || ''));
                        const buttons = dialogs.flatMap(dialog =>
                            [...dialog.querySelectorAll('button')].filter(button =>
                                (button.textContent || '').trim() === '重新登录'));
                        if (buttons.length !== 1) return false;
                        buttons[0].click();
                        return true;
                    })()
                """, cancellationToken);
                if (!clicked)
                {
                    return new LoginContractState(false, "Token 过期弹窗的“重新登录”按钮点击失败");
                }

                _logger.Info("检测到 Token 过期弹窗，已自动点击重新登录");
                await Task.Delay(350, cancellationToken);
                NavigateToLogin();
                await Task.Delay(350, cancellationToken);
                continue;
            }

            if (snapshot.Ready)
            {
                return new LoginContractState(true, null);
            }

            await Task.Delay(250, cancellationToken);
        }

        return new LoginContractState(
            false,
            lastContractError ?? "登录页加载超时或控件契约发生变化");
    }

    private async Task<CaptchaFrame> ReadCaptchaFrameAsync(CancellationToken cancellationToken)
    {
        var source = await ReadCaptchaUriAsync(cancellationToken);
        return TryDecodeDataUri(source, out var image)
            ? new CaptchaFrame(source, image)
            : new CaptchaFrame(source, null, "验证码图片不是可读取的 Data URI");
    }

    private async Task<CaptchaFrame> RefreshAndReadCaptchaFrameAsync(
        string? previousSource,
        CancellationToken cancellationToken)
    {
        var automaticallyRefreshed = await WaitForChangedCaptchaFrameAsync(
            previousSource,
            CaptchaAutoRefreshWait,
            cancellationToken);
        if (automaticallyRefreshed is not null)
        {
            return automaticallyRefreshed;
        }

        var clicked = await ExecuteJsonAsync<bool>("""
            (() => {
                const images = [...document.querySelectorAll('img[alt="点击刷新验证码"]')];
                if (images.length !== 1) return false;
                images[0].click();
                return true;
            })()
        """, cancellationToken);
        if (!clicked)
        {
            return new CaptchaFrame(
                previousSource,
                null,
                "验证码刷新控件不存在或不唯一",
                AutoLoginFailureKind.PageContractChanged,
                true);
        }

        var refreshed = await WaitForChangedCaptchaFrameAsync(
            previousSource,
            CaptchaRefreshTimeout,
            cancellationToken);
        return refreshed ?? new CaptchaFrame(
            previousSource,
            null,
            "刷新后 5 秒内未获得新的验证码图片",
            AutoLoginFailureKind.NetworkUnavailable);
    }

    private async Task<CaptchaFrame?> WaitForChangedCaptchaFrameAsync(
        string? previousSource,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, cancellationToken);
            var frame = await ReadCaptchaFrameAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(frame.SourceId) &&
                frame.Image is { Length: > 0 } &&
                !string.Equals(frame.SourceId, previousSource, StringComparison.Ordinal))
            {
                return frame;
            }
        }

        return null;
    }

    private Task<string?> ReadCaptchaUriAsync(CancellationToken cancellationToken) =>
        ExecuteJsonAsync<string?>("""
            (() => {
                const image = document.querySelector('img[alt="点击刷新验证码"]');
                if (!image) return null;
                if ((image.src || '').startsWith('data:image/')) return image.src;
                try {
                    const canvas = document.createElement('canvas');
                    canvas.width = image.naturalWidth || 240;
                    canvas.height = image.naturalHeight || 80;
                    canvas.getContext('2d').drawImage(image, 0, 0);
                    return canvas.toDataURL('image/png');
                } catch { return null; }
            })()
            """, cancellationToken);

    private Task<bool> FillAndSubmitAsync(
        string username,
        string password,
        string captcha,
        CancellationToken cancellationToken)
    {
        var userJson = JsonSerializer.Serialize(username);
        var passwordJson = JsonSerializer.Serialize(password);
        var captchaJson = JsonSerializer.Serialize(captcha);
        return ExecuteJsonAsync<bool>($$"""
            (() => {
                const setValue = (element, value) => {
                    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                    setter.call(element, value);
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                };
                const input = placeholder => [...document.querySelectorAll('input')]
                    .find(node => node.getAttribute('placeholder') === placeholder);
                const user = input('请输入用户名');
                const password = input('请输入密码');
                const captcha = input('请输入验证码');
                const button = [...document.querySelectorAll('button')]
                    .find(node => (node.textContent || '').includes('登录管理平台'));
                if (!user || !password || !captcha || !button) return false;
                setValue(user, {{userJson}});
                setValue(password, {{passwordJson}});
                setValue(captcha, {{captchaJson}});
                button.click();
                return true;
            })()
        """, cancellationToken);
    }

    private async Task<CaptchaSubmission> SubmitCaptchaAsync(
        LoginAccountProfile account,
        string captcha,
        byte[] image,
        CancellationToken cancellationToken)
    {
        _loginResponseSource = new TaskCompletionSource<LoginResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        LoginResponse response;
        try
        {
            var submitted = await FillAndSubmitAsync(account.Username, account.Password, captcha, cancellationToken);
            if (!submitted)
            {
                return CaptchaSubmission.Complete(new AutoLoginResult(
                    false,
                    AutoLoginFailureKind.PageContractChanged,
                    "登录输入框或提交按钮不存在",
                    true),
                    wasSubmitted: false);
            }

            response = await WaitForLoginResponseAsync(cancellationToken);
        }
        finally
        {
            _loginResponseSource = null;
        }

        if (response.IsTimeout)
        {
            return CaptchaSubmission.Complete(new AutoLoginResult(
                false,
                AutoLoginFailureKind.NetworkUnavailable,
                "登录接口在 12 秒内没有响应",
                CaptchaImage: image,
                CaptchaPrediction: captcha,
                LastResponseCode: response.Code),
                responseCode: response.Code);
        }

        if (response.RequiresSms || await IsSmsPromptVisibleAsync(cancellationToken))
        {
            return CaptchaSubmission.Complete(new AutoLoginResult(
                false,
                AutoLoginFailureKind.SmsRequired,
                response.Message,
                true,
                image,
                captcha,
                LastResponseCode: response.Code),
                responseCode: response.Code);
        }

        if (IsCaptchaError(response.Message))
        {
            return CaptchaSubmission.Retry(response.Message, response.Code);
        }

        if (IsCredentialError(response.Message))
        {
            return CaptchaSubmission.Complete(new AutoLoginResult(
                false,
                AutoLoginFailureKind.CredentialRejected,
                response.Message,
                true,
                image,
                captcha,
                LastResponseCode: response.Code),
                responseCode: response.Code);
        }

        var session = await TryReadCurrentSessionAsync(cancellationToken);
        if (session?.IsValid == true)
        {
            _statusLabel.Text = "自动登录成功。";
            Hide();
            return CaptchaSubmission.Complete(new AutoLoginResult(
                true,
                CaptchaImage: image,
                CaptchaPrediction: captcha,
                LastResponseCode: response.Code),
                responseCode: response.Code);
        }

        var pageError = await ReadPageErrorAsync(cancellationToken);
        if (IsCaptchaError(pageError))
        {
            return CaptchaSubmission.Retry(pageError, response.Code);
        }

        if (IsCredentialError(pageError))
        {
            return CaptchaSubmission.Complete(new AutoLoginResult(
                false,
                AutoLoginFailureKind.CredentialRejected,
                pageError,
                true,
                image,
                captcha,
                LastResponseCode: response.Code),
                responseCode: response.Code);
        }

        var unexpected = pageError ?? response.Message ?? "登录后未获得有效会话";
        return CaptchaSubmission.Complete(new AutoLoginResult(
            false,
            AutoLoginFailureKind.UnexpectedResponse,
            $"登录接口返回未识别结果（code={response.Code}）：{unexpected}",
            true,
            image,
            captcha,
            LastResponseCode: response.Code),
            responseCode: response.Code);
    }

    private async Task<LoginResponse> WaitForLoginResponseAsync(CancellationToken cancellationToken)
    {
        var source = _loginResponseSource;
        if (source is null)
        {
            return LoginResponse.Timeout;
        }

        try
        {
            return await source.Task.WaitAsync(LoginResponseTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return LoginResponse.Timeout;
        }
    }

    private Task<bool> IsSmsPromptVisibleAsync(CancellationToken cancellationToken) =>
        ExecuteJsonAsync<bool>("""
            (() => [...document.querySelectorAll('input')].some(node =>
                (node.getAttribute('placeholder') || '').includes('手机验证码')) ||
                (document.body.innerText || '').includes('异地登录'))()
            """, cancellationToken);

    private Task<string?> ReadPageErrorAsync(CancellationToken cancellationToken) =>
        ExecuteJsonAsync<string?>("""
            (() => [...document.querySelectorAll('.el-message__content, .el-form-item__error')]
                .map(node => (node.textContent || '').trim())
                .filter(Boolean).pop() || null)()
            """, cancellationToken);

    private async Task<T?> ExecuteJsonAsync<T>(string script, CancellationToken cancellationToken)
    {
        var json = await ExecuteScriptAsync(script, cancellationToken);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            _logger.Error("登录页脚本返回了无效 JSON", exception);
            return default;
        }
    }

    private async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task<string?> GetCookieHeaderCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync();
        var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(MonitorDefaults.BaseUrl);
        cancellationToken.ThrowIfCancellationRequested();
        return cookies.Count == 0 ? null : string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    private async Task ValidateSessionAsync()
    {
        if (_checkingSession || !_initialized || !Visible)
        {
            return;
        }

        _checkingSession = true;
        try
        {
            var session = await TryReadCurrentSessionAsync(CancellationToken.None);
            if (session is null)
            {
                _statusLabel.Text = "等待登录 Cookie...";
                return;
            }

            _statusLabel.Text = "登录验证成功。";
            _validationTimer.Stop();
            Hide();
            SessionValidated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"会话验证失败：{exception.Message}";
            _logger.Error("内置登录会话验证失败", exception);
        }
        finally
        {
            _checkingSession = false;
        }
    }

    private async void OnWebResourceResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs eventArgs)
    {
        if (!eventArgs.Request.Uri.Contains("/api/base/login", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var stream = await eventArgs.Response.GetContentAsync();
            if (stream is null)
            {
                return;
            }

            using var reader = new StreamReader(stream);
            var body = await reader.ReadToEndAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeValue)
                ? codeValue.ValueKind switch
                {
                    JsonValueKind.Number when codeValue.TryGetInt32(out var numericCode) => numericCode,
                    JsonValueKind.String when int.TryParse(codeValue.GetString(), out var stringCode) => stringCode,
                    _ => -1
                }
                : -1;
            var message = root.TryGetProperty("msg", out var msgValue) && msgValue.ValueKind == JsonValueKind.String
                ? msgValue.GetString()
                : null;
            var requiresSms = code == 303 ||
                               root.TryGetProperty("data", out var data) &&
                               data.ValueKind == JsonValueKind.Object &&
                               data.TryGetProperty("needcode", out var needCode) &&
                               needCode.ValueKind == JsonValueKind.True;
            _loginResponseSource?.TrySetResult(new LoginResponse(code, message, requiresSms, false));
        }
        catch (Exception exception)
        {
            _logger.Error("读取登录接口响应失败", exception);
        }
    }

    private static bool TryDecodeDataUri(string? dataUri, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(dataUri) || !dataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = dataUri.IndexOf(',');
        if (comma < 0)
        {
            return false;
        }

        try
        {
            bytes = dataUri[(comma + 1)..].Contains(';')
                ? Convert.FromBase64String(dataUri[(comma + 1)..])
                : Convert.FromBase64String(dataUri[(comma + 1)..]);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsCaptchaError(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("验证码", StringComparison.OrdinalIgnoreCase) &&
        !message.Contains("手机", StringComparison.OrdinalIgnoreCase) &&
        !message.Contains("短信", StringComparison.OrdinalIgnoreCase);

    private static bool IsCredentialError(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        (message.Contains("密码", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("账号不存在", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("用户名", StringComparison.OrdinalIgnoreCase));

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    private sealed record LoginContractState(bool Ready, string? Error);

    private sealed record LoginResponse(int Code, string? Message, bool RequiresSms, bool IsTimeout)
    {
        public static LoginResponse Timeout { get; } = new(-1, "登录接口超时", false, true);
    }
}
