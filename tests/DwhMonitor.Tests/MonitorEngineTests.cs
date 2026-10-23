using DwhMonitor.Core;

namespace DwhMonitor.Tests;

public sealed class MonitorEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TerminalLimit_UsesStrictGreaterThanAndSendsOnlyTransitionAndRecovery()
    {
        var api = new FakeDwhApiClient();
        var store = new InMemoryMonitorStore();
        var notifications = new FakeNotificationSink();
        var clock = new FixedClock(Start);
        var current = 1650;
        var vipLimit = 1650;
        api.SnapshotFactory = time => Snapshot(time, current, vipLimit, true);
        var engine = CreateEngine(api, store, notifications, clock);

        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Empty(notifications.Messages);

        current = 1651;
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Single(notifications.Messages);
        Assert.Contains("VIP终端超额告警", notifications.Messages[0].Title);

        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Single(notifications.Messages);

        current = vipLimit;
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Equal(2, notifications.Messages.Count);
        Assert.Contains("VIP终端超额恢复", notifications.Messages[1].Title);

        vipLimit = 1600;
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Equal(3, notifications.Messages.Count);
    }

    [Fact]
    public async Task ActiveAlert_IsNotRepeatedAfterEngineRestart()
    {
        var api = new FakeDwhApiClient
        {
            SnapshotFactory = time => Snapshot(time, 1700, 1650, true)
        };
        var store = new InMemoryMonitorStore();
        var clock = new FixedClock(Start);
        var firstNotifications = new FakeNotificationSink();
        var firstEngine = CreateEngine(api, store, firstNotifications, clock);

        await firstEngine.PollOnceAsync(CancellationToken.None);
        Assert.Single(firstNotifications.Messages);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var secondNotifications = new FakeNotificationSink();
        var secondEngine = CreateEngine(api, store, secondNotifications, clock);
        await secondEngine.PollOnceAsync(CancellationToken.None);

        Assert.Empty(secondNotifications.Messages);
        Assert.True(store.States[AlertKind.TerminalExceeded].Active);
        Assert.False(store.States[AlertKind.TerminalExceeded].NotificationPending);
    }

    [Fact]
    public async Task ApiFailure_AlertsOnThirdFailureAndRecoversAfterOneSuccess()
    {
        var api = new FakeDwhApiClient();
        var store = new InMemoryMonitorStore();
        var notifications = new FakeNotificationSink();
        var clock = new FixedClock(Start);
        var engine = CreateEngine(api, store, notifications, clock);
        await engine.PollOnceAsync(CancellationToken.None);

        api.SnapshotException = new HttpRequestException("模拟网络故障");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(30);
            await engine.PollOnceAsync(CancellationToken.None);
            Assert.Equal(attempt == 3 ? 1 : 0, notifications.Messages.Count);
        }

        Assert.Contains("接口不可达告警", notifications.Messages[0].Title);
        api.SnapshotException = null;
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, notifications.Messages.Count);
        Assert.Contains("接口不可达恢复", notifications.Messages[1].Title);
        Assert.Equal(0, engine.Status.ConsecutiveApiFailures);
    }

    [Fact]
    public async Task HttpTimeout_IsRecordedAsApiFailureWithoutStoppingPolling()
    {
        var api = new FakeDwhApiClient
        {
            SessionException = new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.")
        };
        var store = new InMemoryMonitorStore();
        var engine = CreateEngine(api, store, new FakeNotificationSink(), new FixedClock(Start));

        await engine.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, engine.Status.ConsecutiveApiFailures);
        Assert.Single(store.Samples);
        Assert.Equal(AlertKind.ApiUnavailable.ToString(), store.Samples[0].ErrorKind);
        Assert.Contains("HttpClient.Timeout", store.Samples[0].ErrorMessage);

        api.SessionException = null;
        await engine.PollOnceAsync(CancellationToken.None);

        Assert.Equal(0, engine.Status.ConsecutiveApiFailures);
        Assert.Equal(2, store.Samples.Count);
        Assert.Null(store.Samples[1].ErrorKind);
    }

    [Fact]
    public async Task RunAsync_ContinuesAfterUnexpectedPollException()
    {
        var api = new FakeDwhApiClient();
        var store = new InMemoryMonitorStore();
        var logger = new ListMonitorLogger();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var engine = new MonitorEngine(
            api,
            new FakeCookieProvider(),
            store,
            new FakeNotificationSink(),
            new FixedClock(Start),
            logger);
        var throwOnce = true;
        engine.StatusChanged += (_, status) =>
        {
            if (throwOnce)
            {
                throwOnce = false;
                throw new InvalidOperationException("模拟轮询边界异常");
            }

            if (status.LastSuccessUtc.HasValue)
            {
                cancellation.Cancel();
            }
        };

        await engine.RunAsync(TimeSpan.FromMilliseconds(1), cancellation.Token);

        Assert.Single(store.Samples);
        Assert.Contains(logger.Errors, message => message.Contains("下个周期继续采集"));
    }

    [Fact]
    public async Task SessionExpiry_AlertsImmediatelyAndRecoversAfterLogin()
    {
        var api = new FakeDwhApiClient
        {
            SessionException = new SessionExpiredException("会话已过期")
        };
        var store = new InMemoryMonitorStore();
        var notifications = new FakeNotificationSink();
        var clock = new FixedClock(Start);
        var engine = CreateEngine(api, store, notifications, clock);

        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Single(notifications.Messages);
        Assert.Contains("账号掉线告警", notifications.Messages[0].Title);

        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Single(notifications.Messages);

        api.SessionException = null;
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        await engine.PollOnceAsync(CancellationToken.None);
        Assert.Equal(2, notifications.Messages.Count);
        Assert.Contains("账号掉线恢复", notifications.Messages[1].Title);
        Assert.True(engine.Status.SessionValid);
    }

    [Fact]
    public async Task IndependentConditions_CanAlertTogether()
    {
        var api = new FakeDwhApiClient
        {
            SnapshotFactory = time => Snapshot(time, 1700, 1650, false)
        };
        var notifications = new FakeNotificationSink();
        var engine = CreateEngine(api, new InMemoryMonitorStore(), notifications, new FixedClock(Start));

        await engine.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, notifications.Messages.Count);
        Assert.Contains(notifications.Messages, message => message.Title.Contains("VIP终端超额告警"));
        Assert.Contains(notifications.Messages, message => message.Title.Contains("云机房离线告警"));
        Assert.Equal(2, engine.Status.ActiveAlerts.Count);
    }

    [Fact]
    public async Task AutoLoginFailure_IsIndependentAndRecoversOnce()
    {
        var store = new InMemoryMonitorStore();
        var notifications = new FakeNotificationSink();
        var engine = CreateEngine(new FakeDwhApiClient(), store, notifications, new FixedClock(Start));

        await engine.SetAutoLoginFailureAsync(true, "17700000000", "验证码识别失败", CancellationToken.None);
        Assert.Single(notifications.Messages);
        Assert.Contains("自动登录告警", notifications.Messages[0].Title);
        Assert.Contains("17700000000", notifications.Messages[0].MarkdownBody);

        await engine.SetAutoLoginFailureAsync(true, "17700000000", "验证码识别失败", CancellationToken.None);
        Assert.Single(notifications.Messages);

        await engine.SetAutoLoginFailureAsync(false, "17700000000", null, CancellationToken.None);
        Assert.Equal(2, notifications.Messages.Count);
        Assert.Contains("自动登录恢复", notifications.Messages[1].Title);
    }

    private static MonitorEngine CreateEngine(
        IDwhApiClient api,
        IMonitorStore store,
        INotificationSink notifications,
        IClock clock) => new(
            api,
            new FakeCookieProvider(),
            store,
            notifications,
            clock,
            apiFailureThreshold: 3,
            historyRetentionDays: 90);

    private static DwhSnapshot Snapshot(DateTimeOffset time, int current, int vipLimit, bool online) => new(
        time,
        MonitorDefaults.TargetGuid,
        "云机房",
        online,
        current,
        4293,
        vipLimit,
        10000);
}
