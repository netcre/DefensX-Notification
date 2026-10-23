namespace DwhMonitor.Core;

public static class MonitorDefaults
{
    public const string BaseUrl = "https://www.dwhips.com";
    public const long TargetGuid = 2547092390;
    public const int PollIntervalSeconds = 30;
    public const int ApiFailureThreshold = 3;
    public const int HistoryRetentionDays = 90;
}

public enum AlertKind
{
    TerminalExceeded,
    SessionExpired,
    NetbarOffline,
    ApiUnavailable,
    AutoLoginFailed
}

public sealed record SessionUserInfo(bool IsValid, string? Account);

public sealed record DwhSnapshot(
    DateTimeOffset SampledAtUtc,
    long TargetGuid,
    string NetbarName,
    bool Online,
    int TerminalCurrent,
    int TerminalTotal,
    int VipLimit,
    int DefenseCount);

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
{
    public static SampleRecord FromSnapshot(DwhSnapshot snapshot) => new(
        snapshot.SampledAtUtc,
        snapshot.TargetGuid,
        snapshot.NetbarName,
        snapshot.Online,
        snapshot.TerminalCurrent,
        snapshot.TerminalTotal,
        snapshot.VipLimit,
        snapshot.DefenseCount,
        true,
        null,
        null);
}

public sealed record NotificationMessage(string Title, string MarkdownBody);

public sealed record NotificationResult(bool Success, string? Error = null)
{
    public static NotificationResult Ok() => new(true);
    public static NotificationResult Failed(string error) => new(false, error);
}

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

public sealed record MonitorRuntimeStatus(
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    DwhSnapshot? LastSnapshot,
    bool SessionValid,
    int ConsecutiveApiFailures,
    string? LastError,
    IReadOnlySet<AlertKind> ActiveAlerts)
{
    public static MonitorRuntimeStatus Starting { get; } = new(
        null,
        null,
        null,
        false,
        0,
        null,
        new HashSet<AlertKind>());
}

public sealed record DingTalkCredentials(string Webhook, string Secret);

public interface ICookieProvider
{
    Task<string?> GetCookieHeaderAsync(CancellationToken cancellationToken);
}

public interface INotificationSink
{
    Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IMonitorLogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullMonitorLogger : IMonitorLogger
{
    public void Info(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
