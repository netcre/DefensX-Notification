using DwhMonitor.Core;

namespace DwhMonitor.Tests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class FakeCookieProvider : ICookieProvider
{
    public Task<string?> GetCookieHeaderAsync(CancellationToken cancellationToken) =>
        Task.FromResult<string?>("session=test");
}

internal sealed class FakeNotificationSink : INotificationSink
{
    public List<NotificationMessage> Messages { get; } = [];
    public bool Succeed { get; set; } = true;

    public Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.FromResult(Succeed
            ? NotificationResult.Ok()
            : NotificationResult.Failed("模拟发送失败"));
    }
}

internal sealed class ListMonitorLogger : IMonitorLogger
{
    public List<string> Errors { get; } = [];

    public void Info(string message) { }

    public void Error(string message, Exception? exception = null) =>
        Errors.Add(exception is null ? message : $"{message}: {exception.Message}");
}

internal sealed class FakeDwhApiClient : IDwhApiClient
{
    public Exception? SessionException { get; set; }
    public Func<DateTimeOffset, DwhSnapshot> SnapshotFactory { get; set; } = time => new DwhSnapshot(
        time,
        MonitorDefaults.TargetGuid,
        "云机房",
        true,
        1000,
        4293,
        1650,
        10000);
    public Exception? SnapshotException { get; set; }

    public Task CheckSessionAsync(string cookieHeader, CancellationToken cancellationToken)
    {
        if (SessionException is not null)
        {
            throw SessionException;
        }

        return Task.CompletedTask;
    }

    public Task<SessionUserInfo> GetSessionInfoAsync(string cookieHeader, CancellationToken cancellationToken)
    {
        if (SessionException is not null)
        {
            throw SessionException;
        }

        return Task.FromResult(new SessionUserInfo(true, "test"));
    }

    public Task<DwhSnapshot> GetSnapshotAsync(
        string cookieHeader,
        DateTimeOffset sampledAtUtc,
        CancellationToken cancellationToken)
    {
        if (SnapshotException is not null)
        {
            throw SnapshotException;
        }

        return Task.FromResult(SnapshotFactory(sampledAtUtc));
    }
}

internal sealed class InMemoryMonitorStore : IMonitorStore
{
    public List<SampleRecord> Samples { get; } = [];
    public Dictionary<AlertKind, AlertState> States { get; } = [];
    public DwhSnapshot? LastSnapshot { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InsertSampleAsync(SampleRecord sample, CancellationToken cancellationToken)
    {
        Samples.Add(sample);
        if (sample.ErrorKind is null)
        {
            LastSnapshot = new DwhSnapshot(
                sample.SampledAtUtc,
                sample.TargetGuid,
                sample.NetbarName!,
                sample.Online!.Value,
                sample.TerminalCurrent!.Value,
                sample.TerminalTotal!.Value,
                sample.VipLimit!.Value,
                sample.DefenseCount!.Value);
        }

        return Task.CompletedTask;
    }

    public Task<DwhSnapshot?> GetLastSuccessfulSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(LastSnapshot);

    public Task<IReadOnlyDictionary<AlertKind, AlertState>> LoadAlertStatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<AlertKind, AlertState>>(new Dictionary<AlertKind, AlertState>(States));

    public Task SaveAlertStateAsync(AlertState state, CancellationToken cancellationToken)
    {
        States[state.Kind] = state;
        return Task.CompletedTask;
    }

    public Task<int> CleanupSamplesAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var removed = Samples.RemoveAll(sample => sample.SampledAtUtc < cutoffUtc);
        return Task.FromResult(removed);
    }
}
