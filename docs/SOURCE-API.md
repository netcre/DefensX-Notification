# 当前源码声明索引

[API](API.md) · [开发](DEVELOPMENT.md)

本页由当前 C# 源码提取，记录声明而非稳定外部 SDK；正文行为/错误/副作用见 API。方法体不复制。源文件行号以本次发布源码为准。

## src/DwhMonitor.App/AppPaths.cs

```csharp
// line 3
internal sealed record AppPathSet(
    string RootDirectory,
    string DataDirectory,
    string LogDirectory,
    string WebViewDirectory,
    string DatabasePath,
    string SettingsPath);

// line 11
internal sealed record AppDataMigrationResult(
    int CopiedFiles,
    int SkippedFiles,
    IReadOnlyList<string> Errors)

// line 16
    public bool HasErrors => Errors.Count != 0;

// line 19
internal static class AppPaths

// line 34
    public static string RootDirectory => RuntimePaths.RootDirectory;

// line 35
    public static string DataDirectory => RuntimePaths.DataDirectory;

// line 36
    public static string LogDirectory => RuntimePaths.LogDirectory;

// line 37
    public static string WebViewDirectory => RuntimePaths.WebViewDirectory;

// line 38
    public static string DatabasePath => RuntimePaths.DatabasePath;

// line 39
    public static string SettingsPath => RuntimePaths.SettingsPath;

// line 40
    public static string LegacyRootDirectory => LegacyRoot;

// line 41
    public static AppDataMigrationResult? LastMigrationResult => _lastMigrationResult;

// line 43
    internal static AppPathSet BuildForRoot(string root)

// line 61
    public static void EnsureCreated()

// line 85
    internal static AppDataMigrationResult MigrateLegacyRoot(string legacyRoot, string runtimeRoot)
```

## src/DwhMonitor.App/AppSettingsStore.cs

```csharp
// line 8
internal sealed record StoredLoginAccount(
    string Id,
    string ProtectedUsername,
    string ProtectedPassword,
    DateTimeOffset? LastVerifiedUtc = null);

// line 14
internal sealed record StoredAppSettings(
    string? ProtectedWebhook = null,
    string? ProtectedSecret = null,
    bool AutoStartEnabled = true,
    bool AutoLoginEnabled = false,
    string? ActiveAccountId = null,
    List<StoredLoginAccount>? LoginAccounts = null);

// line 22
internal sealed record LoginAccountProfile(
    string Id,
    string Username,
    string Password,
    DateTimeOffset? LastVerifiedUtc = null);

// line 28
internal sealed class AppSettingsStore

// line 36
    public AppSettingsStore(string? settingsPath = null)

// line 42
    public bool AutoStartEnabled => _settings.AutoStartEnabled;

// line 43
    public bool AutoLoginEnabled => _settings.AutoLoginEnabled;

// line 44
    public string? ActiveAccountId => _settings.ActiveAccountId;

// line 45
    public bool HasCredentials => GetCredentials() is not null;

// line 46
    public bool HasLoginAccount => GetLoginAccounts().Count > 0;

// line 48
    public DingTalkCredentials? GetCredentials()

// line 68
    public IReadOnlyList<LoginAccountProfile> GetLoginAccounts()

// line 93
    public LoginAccountProfile? GetActiveLoginAccount() =>

// line 97
    public void SaveCredentials(string webhook, string secret, bool autoStartEnabled)

// line 108
    public void SaveLoginSettings(
        IReadOnlyCollection<LoginAccountProfile> accounts,
        string? activeAccountId,
        bool autoLoginEnabled)

// line 135
    public void MarkLoginAccountVerified(string accountId, DateTimeOffset verifiedUtc)

// line 143
    public void SetAutoStart(bool enabled)
```

## src/DwhMonitor.App/AutoLoginModels.cs

```csharp
// line 3
internal enum AutoLoginFailureKind
{
    CredentialRejected,
    CaptchaExhausted,
    NetworkUnavailable,
    SmsRequired,
    PageContractChanged,
    UnexpectedResponse
}

// line 13
internal sealed record AutoLoginResult(
    bool Success,
    AutoLoginFailureKind? FailureKind = null,
    string? Error = null,
    bool RequiresUserAction = false,
    byte[]? CaptchaImage = null,
    string? CaptchaPrediction = null,
    int SubmittedCaptchaAttempts = 0,
    int CaptchaRefreshes = 0,
    int UniqueCaptchaImages = 0,
    int? LastResponseCode = null);
```

## src/DwhMonitor.App/AutostartManager.cs

```csharp
// line 5
internal static class AutostartManager

// line 10
    public static void SetEnabled(bool enabled)

// line 26
    public static bool IsEnabled()
```

## src/DwhMonitor.App/CaptchaPrimaryModel.cs

```csharp
// line 6
internal sealed class CaptchaPrimaryModel

// line 27
    public CaptchaPrimaryModel(string path)

// line 71
    public string? Recognize(byte[] pngBytes)
```

## src/DwhMonitor.App/CaptchaRecognizer.cs

```csharp
// line 3
internal sealed class CaptchaRecognizer : IDisposable

// line 5
    internal const bool ReleaseAccuracyGatePassed = true;

// line 9
    public CaptchaRecognizer(string applicationDirectory)

// line 21
    public string? Recognize(byte[] pngBytes)

// line 27
    public void Dispose()
```

## src/DwhMonitor.App/CaptchaRetryCoordinator.cs

```csharp
// line 3
internal sealed record CaptchaFrame(
    string? SourceId,
    byte[]? Image,
    string? Error = null,
    AutoLoginFailureKind? FatalFailureKind = null,
    bool RequiresUserAction = false);

// line 10
internal sealed record CaptchaSubmission(
    bool WasSubmitted,
    AutoLoginResult? Result,
    string? RetryError = null,
    int? ResponseCode = null)

// line 16
    public static CaptchaSubmission Retry(string? error, int? responseCode = null) =>

// line 19
    public static CaptchaSubmission Complete(
        AutoLoginResult result,
        bool wasSubmitted = true,
        int? responseCode = null) => new(wasSubmitted, result, ResponseCode: responseCode);

// line 25
internal static class CaptchaRetryCoordinator

// line 27
    internal const int MaximumAttempts = 5;

// line 29
    public static async Task<AutoLoginResult> RunAsync(
        int maximumSubmissions,
        Func<CancellationToken, Task<CaptchaFrame>> readInitialCaptchaAsync,
        Func<string?, CancellationToken, Task<CaptchaFrame>> refreshCaptchaAsync,
        Func<byte[], string?> recognize,
        Func<string, byte[], CancellationToken, Task<CaptchaSubmission>> submitAsync,
        CancellationToken cancellationToken)
```

## src/DwhMonitor.App/FileMonitorLogger.cs

```csharp
// line 6
internal sealed partial class FileMonitorLogger : IMonitorLogger

// line 10
    public FileMonitorLogger()

// line 15
    public void Info(string message) => Write("INFO", message, null);

// line 17
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);
```

## src/DwhMonitor.App/LoginPageRecovery.cs

```csharp
// line 3
internal enum LoginPageRecoveryAction
{
    Wait,
    ClickExpiredTokenRelogin,
    ContractChanged
}

// line 10
internal sealed record LoginPageSnapshot(
    bool Ready,
    bool TokenExpiredDialogVisible,
    int ReloginButtonCount,
    string? ContractError = null);

// line 16
internal sealed record LoginPageRecoveryDecision(
    LoginPageRecoveryAction Action,
    string? Error = null);

// line 20
internal static class LoginPageRecovery

// line 22
    public static LoginPageRecoveryDecision Decide(LoginPageSnapshot snapshot)
```

## src/DwhMonitor.App/OcrAcceptanceApplicationContext.cs

```csharp
// line 9
internal sealed class OcrAcceptanceApplicationContext : ApplicationContext

// line 20
    public OcrAcceptanceApplicationContext(int requestedTrials, bool captureSamples)
```

## src/DwhMonitor.App/SessionWebViewForm.cs

```csharp
// line 8
internal sealed class SessionWebViewForm : Form, ICookieProvider

// line 31
    public SessionWebViewForm(IDwhApiClient apiClient, IMonitorLogger logger)

// line 72
    public event EventHandler? SessionValidated;

// line 74
    public async Task InitializeAsync()

// line 102
    public async Task ShowLoginAsync()

// line 113
    public async Task ShowForUserActionAsync(string message)

// line 123
    public async Task ClearSiteDataAsync(CancellationToken cancellationToken)

// line 136
    public async Task<AutoLoginResult> TryAutoLoginAsync(
        LoginAccountProfile account,
        CaptchaRecognizer recognizer,
        CancellationToken cancellationToken,
        int maximumCaptchaAttempts = 5)

// line 164
    public void MarkSessionInvalid(string? message)

// line 175
    public Task<string?> GetCookieHeaderAsync(CancellationToken cancellationToken)

// line 202
    public async Task<SessionUserInfo?> TryReadCurrentSessionAsync(CancellationToken cancellationToken)

// line 220
    public void Shutdown()

// line 776
        public static LoginResponse Timeout
```

## src/DwhMonitor.App/SettingsForm.cs

```csharp
// line 5
internal sealed class SettingsForm : Form

// line 34
    public SettingsForm(
        AppSettingsStore settingsStore,
        Func<DingTalkCredentials, CancellationToken, Task<NotificationResult>> testSender)
```

## src/DwhMonitor.App/StatusForm.cs

```csharp
// line 5
internal sealed class StatusForm : Form

// line 10
    public StatusForm()

// line 53
    public void UpdateStatus(MonitorRuntimeStatus status)

// line 76
    public void Shutdown()
```

## src/DwhMonitor.App/TrayApplicationContext.cs

```csharp
// line 7
internal sealed class TrayApplicationContext : ApplicationContext

// line 27
    public TrayApplicationContext()
```

## src/DwhMonitor.App/TrayIconFactory.cs

```csharp
// line 5
internal static class TrayIconFactory

// line 7
    public static Icon Create(Color color)
```

## src/DwhMonitor.Core/AlertStateMachine.cs

```csharp
// line 3
public static class AlertStateMachine

// line 13
    public static AlertState ApplyCondition(
        AlertState? current,
        AlertKind kind,
        bool active,
        DateTimeOffset now,
        NotificationMessage message)

// line 41
    public static AlertState MarkSent(AlertState state, DateTimeOffset now) => state with

// line 51
    public static AlertState MarkFailed(AlertState state, DateTimeOffset now)
```

## src/DwhMonitor.Core/DingTalkNotifier.cs

```csharp
// line 7
public static class DingTalkSigner

// line 9
    public static Uri BuildSignedUri(string webhook, string secret, long timestampMilliseconds)

// line 29
public sealed class DingTalkNotifier(
    HttpClient httpClient,
    Func<DingTalkCredentials?> credentialsProvider,
    IClock? clock = null) : INotificationSink

// line 38
    public async Task<NotificationResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken)
```

## src/DwhMonitor.Core/DwhApiClient.cs

```csharp
// line 7
public interface IDwhApiClient
{
    Task CheckSessionAsync(string cookieHeader, CancellationToken cancellationToken);
    Task<SessionUserInfo> GetSessionInfoAsync(string cookieHeader, CancellationToken cancellationToken);
    Task<DwhSnapshot> GetSnapshotAsync(string cookieHeader, DateTimeOffset sampledAtUtc, CancellationToken cancellationToken);
}

// line 14
public sealed class SessionExpiredException(string message) : Exception(message);

// line 15
public sealed class TargetNotFoundException(string message) : Exception(message);

// line 17
public sealed class DwhApiClient(HttpClient httpClient, long targetGuid = MonitorDefaults.TargetGuid) : IDwhApiClient

// line 23
    public async Task CheckSessionAsync(string cookieHeader, CancellationToken cancellationToken)

// line 28
    public async Task<SessionUserInfo> GetSessionInfoAsync(string cookieHeader, CancellationToken cancellationToken)

// line 50
    public async Task<DwhSnapshot> GetSnapshotAsync(
        string cookieHeader,
        DateTimeOffset sampledAtUtc,
        CancellationToken cancellationToken)

// line 78
    public static DwhSnapshot ParseSnapshotJson(string json, long targetGuid, DateTimeOffset sampledAtUtc)
```

## src/DwhMonitor.Core/Models.cs

```csharp
// line 3
public static class MonitorDefaults

// line 5
    public const string BaseUrl = "https://www.dwhips.com";

// line 6
    public const long TargetGuid = 2547092390;

// line 7
    public const int PollIntervalSeconds = 30;

// line 8
    public const int ApiFailureThreshold = 3;

// line 9
    public const int HistoryRetentionDays = 90;

// line 12
public enum AlertKind
{
    TerminalExceeded,
    SessionExpired,
    NetbarOffline,
    ApiUnavailable,
    AutoLoginFailed
}

// line 21
public sealed record SessionUserInfo(bool IsValid, string? Account);

// line 23
public sealed record DwhSnapshot(
    DateTimeOffset SampledAtUtc,
    long TargetGuid,
    string NetbarName,
    bool Online,
    int TerminalCurrent,
    int TerminalTotal,
    int VipLimit,
    int DefenseCount);

// line 33
public sealed record SampleRecord(
    DateTimeOffset SampledAtUtc,
    long TargetGuid,
    string? NetbarName,
    bool? Online,
    int? TerminalCurrent,
    int? TerminalTotal,
    int? VipLimit,
    int? DefenseCount,
    bool? AuthOk,
    string? ErrorKind,
    string? ErrorMessage)

// line 46
    public static SampleRecord FromSnapshot(DwhSnapshot snapshot) => new(

// line 60
public sealed record NotificationMessage(string Title, string MarkdownBody);

// line 62
public sealed record NotificationResult(bool Success, string? Error = null)

// line 64
    public static NotificationResult Ok() => new(true);

// line 65
    public static NotificationResult Failed(string error) => new(false, error);

// line 68
public sealed record AlertState(
    AlertKind Kind,
    bool Active,
    DateTimeOffset? FirstSeenUtc,
    DateTimeOffset LastChangeUtc,
    DateTimeOffset? LastNotifiedUtc,
    bool NotificationPending,
    bool PendingRecovery,
    int RetryCount,
    DateTimeOffset? NextRetryUtc,
    string? PendingTitle,
    string? PendingBody);

// line 81
public sealed record MonitorRuntimeStatus(
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    DwhSnapshot? LastSnapshot,
    bool SessionValid,
    int ConsecutiveApiFailures,
    string? LastError,
    IReadOnlySet<AlertKind> ActiveAlerts)

// line 90
    public static MonitorRuntimeStatus Starting

// line 100
public sealed record DingTalkCredentials(string Webhook, string Secret);

// line 102
public interface ICookieProvider
{
    Task<string?> GetCookieHeaderAsync(CancellationToken cancellationToken);
}

// line 107
public interface INotificationSink
{
    Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}

// line 112
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

// line 117
public sealed class SystemClock : IClock

// line 119
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

// line 122
public interface IMonitorLogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

// line 128
public sealed class NullMonitorLogger : IMonitorLogger

// line 130
    public void Info(string message)

// line 131
    public void Error(string message, Exception? exception = null)
```

## src/DwhMonitor.Core/MonitorEngine.cs

```csharp
// line 3
public sealed class MonitorEngine(
    IDwhApiClient apiClient,
    ICookieProvider cookieProvider,
    IMonitorStore store,
    INotificationSink notificationSink,
    IClock? clock = null,
    IMonitorLogger? logger = null,
    int apiFailureThreshold = MonitorDefaults.ApiFailureThreshold,
    int historyRetentionDays = MonitorDefaults.HistoryRetentionDays)

// line 28
    public event EventHandler<MonitorRuntimeStatus>? StatusChanged;

// line 30
    public MonitorRuntimeStatus Status

// line 32
    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)

// line 65
    public async Task InitializeAsync(CancellationToken cancellationToken)

// line 84
    public async Task PollOnceAsync(CancellationToken cancellationToken)

// line 154
    public async Task SetAutoLoginFailureAsync(
        bool active,
        string account,
        string? error,
        CancellationToken cancellationToken)
```

## src/DwhMonitor.Core/MonitorStore.cs

```csharp
// line 6
public interface IMonitorStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task InsertSampleAsync(SampleRecord sample, CancellationToken cancellationToken);
    Task<DwhSnapshot?> GetLastSuccessfulSnapshotAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<AlertKind, AlertState>> LoadAlertStatesAsync(CancellationToken cancellationToken);
    Task SaveAlertStateAsync(AlertState state, CancellationToken cancellationToken);
    Task<int> CleanupSamplesAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}

// line 16
public sealed class SqliteMonitorStore : IMonitorStore

// line 20
    public SqliteMonitorStore(string databasePath)

// line 32
    public string DatabasePath

// line 34
    public async Task InitializeAsync(CancellationToken cancellationToken)

// line 78
    public async Task InsertSampleAsync(SampleRecord sample, CancellationToken cancellationToken)

// line 106
    public async Task<DwhSnapshot?> GetLastSuccessfulSnapshotAsync(CancellationToken cancellationToken)

// line 135
    public async Task<IReadOnlyDictionary<AlertKind, AlertState>> LoadAlertStatesAsync(
        CancellationToken cancellationToken)

// line 172
    public async Task SaveAlertStateAsync(AlertState state, CancellationToken cancellationToken)

// line 211
    public async Task<int> CleanupSamplesAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
```

## 训练器命令参数

Python 训练不是生产运行必需；版本未锁定。参数默认值直接取自当前脚本，完整语义以对应脚本为准。

### train_alignment_cnn.py

```python
parser.add_argument("--epochs", type=int, default=18)
parser.add_argument("--steps-per-epoch", type=int, default=80)
parser.add_argument("--batch-size", type=int, default=32)
parser.add_argument("--real-fine-tune-steps", type=int, default=400)
parser.add_argument("--online-sample-dir", type=Path, required=True)
```

### train_primary_crnn.py

```python
parser.add_argument("--epochs", type=int, default=30)
parser.add_argument("--steps-per-epoch", type=int, default=100)
parser.add_argument("--batch-size", type=int, default=32)
parser.add_argument("--real-fine-tune-steps", type=int, default=600)
parser.add_argument("--online-sample-dir", type=Path)
parser.add_argument("--online-fine-tune-steps", type=int, default=600)
parser.add_argument("--online-learning-rate", type=float, default=0.00003)
parser.add_argument("--hard-sample-dir", type=Path)
parser.add_argument("--hard-fine-tune-steps", type=int, default=80)
parser.add_argument("--hard-learning-rate", type=float, default=0.0002)
```

### train_sequence_crnn.py

```python
parser.add_argument("--epochs", type=int, default=12)
parser.add_argument("--steps-per-epoch", type=int, default=60)
parser.add_argument("--batch-size", type=int, default=20)
parser.add_argument("--real-fine-tune-steps", type=int, default=300)
parser.add_argument("--online-sample-dir", type=Path, required=True)
parser.add_argument("--resume", type=Path)
parser.add_argument("--learning-rate", type=float, default=0.001)
```
