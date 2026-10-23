namespace DwhMonitor.Core;

public sealed class MonitorEngine(
    IDwhApiClient apiClient,
    ICookieProvider cookieProvider,
    IMonitorStore store,
    INotificationSink notificationSink,
    IClock? clock = null,
    IMonitorLogger? logger = null,
    int apiFailureThreshold = MonitorDefaults.ApiFailureThreshold,
    int historyRetentionDays = MonitorDefaults.HistoryRetentionDays)
{
    private readonly IDwhApiClient _apiClient = apiClient;
    private readonly ICookieProvider _cookieProvider = cookieProvider;
    private readonly IMonitorStore _store = store;
    private readonly INotificationSink _notificationSink = notificationSink;
    private readonly IClock _clock = clock ?? new SystemClock();
    private readonly IMonitorLogger _logger = logger ?? new NullMonitorLogger();
    private readonly int _apiFailureThreshold = apiFailureThreshold;
    private readonly int _historyRetentionDays = historyRetentionDays;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly Dictionary<AlertKind, AlertState> _alertStates = [];
    private bool _initialized;
    private int _consecutiveApiFailures;
    private DateTimeOffset? _lastCleanupUtc;
    private DwhSnapshot? _lastSnapshot;

    public event EventHandler<MonitorRuntimeStatus>? StatusChanged;

    public MonitorRuntimeStatus Status { get; private set; } = MonitorRuntimeStatus.Starting;

    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        using var timer = new PeriodicTimer(interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.Error("监控轮询发生未处理异常，下个周期继续采集", exception);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _store.InitializeAsync(cancellationToken);
        var persistedStates = await _store.LoadAlertStatesAsync(cancellationToken);
        foreach (var (kind, state) in persistedStates)
        {
            _alertStates[kind] = state;
        }

        _lastSnapshot = await _store.GetLastSuccessfulSnapshotAsync(cancellationToken);
        _initialized = true;
        PublishStatus(null, false, null);
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        await _pollLock.WaitAsync(cancellationToken);

        try
        {
            await InitializeAsync(cancellationToken);
            var now = _clock.UtcNow;
            string? cookieHeader;
            try
            {
                cookieHeader = await _cookieProvider.GetCookieHeaderAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(cookieHeader))
                {
                    throw new SessionExpiredException("内置登录会话中没有可用 Cookie");
                }

                await _apiClient.CheckSessionAsync(cookieHeader, cancellationToken);
                await ApplyConditionAsync(AlertKind.SessionExpired, false, now, null, cancellationToken);
            }
            catch (SessionExpiredException exception)
            {
                await HandleSessionExpiredAsync(now, exception.Message, cancellationToken);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                await HandleApiFailureAsync(now, null, exception, cancellationToken);
                return;
            }

            try
            {
                var snapshot = await _apiClient.GetSnapshotAsync(cookieHeader, now, cancellationToken);
                _consecutiveApiFailures = 0;
                _lastSnapshot = snapshot;
                await _store.InsertSampleAsync(SampleRecord.FromSnapshot(snapshot), cancellationToken);
                await ApplyConditionAsync(AlertKind.ApiUnavailable, false, now, null, cancellationToken);
                await ApplyConditionAsync(
                    AlertKind.TerminalExceeded,
                    snapshot.TerminalCurrent > snapshot.VipLimit,
                    now,
                    null,
                    cancellationToken);
                await ApplyConditionAsync(
                    AlertKind.NetbarOffline,
                    !snapshot.Online,
                    now,
                    null,
                    cancellationToken);
                await CleanupIfDueAsync(now, cancellationToken);
                await ProcessPendingNotificationsAsync(now, cancellationToken);
                PublishStatus(now, true, null);
                _logger.Info($"采集成功：{snapshot.NetbarName} {snapshot.TerminalCurrent}/{snapshot.TerminalTotal}，VIP {snapshot.VipLimit}");
            }
            catch (SessionExpiredException exception)
            {
                await HandleSessionExpiredAsync(now, exception.Message, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                await HandleApiFailureAsync(now, true, exception, cancellationToken);
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    public async Task SetAutoLoginFailureAsync(
        bool active,
        string account,
        string? error,
        CancellationToken cancellationToken)
    {
        await _pollLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            var now = _clock.UtcNow;
            var message = BuildAutoLoginMessage(active, account, now, error);
            await ApplyConditionAsync(AlertKind.AutoLoginFailed, active, now, error, cancellationToken, message);
            await ProcessPendingNotificationsAsync(now, cancellationToken);
            PublishStatus(now, Status.SessionValid, error);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task HandleSessionExpiredAsync(
        DateTimeOffset now,
        string error,
        CancellationToken cancellationToken)
    {
        _consecutiveApiFailures = 0;
        await _store.InsertSampleAsync(new SampleRecord(
            now,
            MonitorDefaults.TargetGuid,
            _lastSnapshot?.NetbarName,
            _lastSnapshot?.Online,
            _lastSnapshot?.TerminalCurrent,
            _lastSnapshot?.TerminalTotal,
            _lastSnapshot?.VipLimit,
            _lastSnapshot?.DefenseCount,
            false,
            AlertKind.SessionExpired.ToString(),
            error), cancellationToken);
        await ApplyConditionAsync(AlertKind.SessionExpired, true, now, error, cancellationToken);
        await ProcessPendingNotificationsAsync(now, cancellationToken);
        PublishStatus(now, false, error);
        _logger.Error(error);
    }

    private async Task HandleApiFailureAsync(
        DateTimeOffset now,
        bool? authOk,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _consecutiveApiFailures++;
        var error = exception.Message;
        await _store.InsertSampleAsync(new SampleRecord(
            now,
            MonitorDefaults.TargetGuid,
            _lastSnapshot?.NetbarName,
            _lastSnapshot?.Online,
            _lastSnapshot?.TerminalCurrent,
            _lastSnapshot?.TerminalTotal,
            _lastSnapshot?.VipLimit,
            _lastSnapshot?.DefenseCount,
            authOk,
            AlertKind.ApiUnavailable.ToString(),
            error), cancellationToken);
        if (_consecutiveApiFailures >= _apiFailureThreshold)
        {
            await ApplyConditionAsync(AlertKind.ApiUnavailable, true, now, error, cancellationToken);
        }

        await ProcessPendingNotificationsAsync(now, cancellationToken);
        PublishStatus(now, authOk == true, error);
        _logger.Error($"接口采集失败（连续 {_consecutiveApiFailures} 次）：{error}", exception);
    }

    private async Task ApplyConditionAsync(
        AlertKind kind,
        bool active,
        DateTimeOffset now,
        string? error,
        CancellationToken cancellationToken,
        NotificationMessage? messageOverride = null)
    {
        _alertStates.TryGetValue(kind, out var current);
        var message = messageOverride ?? BuildMessage(kind, active, now, error);
        var next = AlertStateMachine.ApplyCondition(current, kind, active, now, message);
        if (next == current)
        {
            return;
        }

        _alertStates[kind] = next;
        await _store.SaveAlertStateAsync(next, cancellationToken);
    }

    private async Task ProcessPendingNotificationsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var state in _alertStates.Values.Where(state =>
                     state.NotificationPending &&
                     state.NextRetryUtc <= now &&
                     state.PendingTitle is not null &&
                     state.PendingBody is not null).ToArray())
        {
            var message = new NotificationMessage(state.PendingTitle!, state.PendingBody!);
            var result = await _notificationSink.SendAsync(message, cancellationToken);
            var updated = result.Success
                ? AlertStateMachine.MarkSent(state, now)
                : AlertStateMachine.MarkFailed(state, now);
            _alertStates[state.Kind] = updated;
            await _store.SaveAlertStateAsync(updated, cancellationToken);
            if (result.Success)
            {
                _logger.Info($"钉钉通知发送成功：{message.Title}");
            }
            else
            {
                _logger.Error(result.Error ?? "钉钉通知发送失败");
            }
        }
    }

    private async Task CleanupIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_lastCleanupUtc.HasValue && now - _lastCleanupUtc.Value < TimeSpan.FromDays(1))
        {
            return;
        }

        var deleted = await _store.CleanupSamplesAsync(now.AddDays(-_historyRetentionDays), cancellationToken);
        _lastCleanupUtc = now;
        if (deleted > 0)
        {
            _logger.Info($"已清理 {deleted} 条过期采样记录");
        }
    }

    private NotificationMessage BuildMessage(AlertKind kind, bool active, DateTimeOffset now, string? error)
    {
        var label = kind switch
        {
            AlertKind.TerminalExceeded => "VIP终端超额",
            AlertKind.SessionExpired => "后台账号掉线",
            AlertKind.NetbarOffline => "云机房离线",
            AlertKind.ApiUnavailable => "盾网接口不可达",
            AlertKind.AutoLoginFailed => "自动登录失败",
            _ => kind.ToString()
        };
        var stateText = active ? "告警" : "恢复";
        var snapshot = _lastSnapshot;
        var localTime = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        var lines = new List<string>
        {
            $"### [盾网监控] {label}{stateText}",
            $"- 网吧：{snapshot?.NetbarName ?? "云机房"}",
            $"- GUID：{MonitorDefaults.TargetGuid}",
            $"- 当前终端：{snapshot?.TerminalCurrent.ToString() ?? "未知"}",
            $"- 终端总数：{snapshot?.TerminalTotal.ToString() ?? "未知"}",
            $"- VIP额度：{snapshot?.VipLimit.ToString() ?? "未知"}",
            $"- 网吧状态：{(snapshot is null ? "未知" : snapshot.Online ? "在线" : "离线")}",
            $"- 检测时间：{localTime}"
        };
        if (!string.IsNullOrWhiteSpace(error))
        {
            lines.Add($"- 错误摘要：{error}");
        }

        return new NotificationMessage($"[盾网监控] {label}{stateText}", string.Join("\n\n", lines));
    }

    private NotificationMessage BuildAutoLoginMessage(
        bool active,
        string account,
        DateTimeOffset now,
        string? error)
    {
        var stateText = active ? "告警" : "恢复";
        var snapshot = _lastSnapshot;
        var localTime = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        var lines = new List<string>
        {
            $"### [盾网监控] 自动登录{stateText}",
            $"- 账号：{account}",
            $"- 网吧：{snapshot?.NetbarName ?? "云机房"}",
            $"- GUID：{MonitorDefaults.TargetGuid}",
            $"- 当前终端：{snapshot?.TerminalCurrent.ToString() ?? "未知"}",
            $"- 终端总数：{snapshot?.TerminalTotal.ToString() ?? "未知"}",
            $"- VIP额度：{snapshot?.VipLimit.ToString() ?? "未知"}",
            $"- 网吧状态：{(snapshot is null ? "未知" : snapshot.Online ? "在线" : "离线")}",
            $"- 检测时间：{localTime}"
        };
        if (!string.IsNullOrWhiteSpace(error))
        {
            lines.Add($"- 错误摘要：{error}");
        }

        return new NotificationMessage($"[盾网监控] 自动登录{stateText}", string.Join("\n\n", lines));
    }

    private void PublishStatus(DateTimeOffset? attemptUtc, bool sessionValid, string? error)
    {
        var activeAlerts = _alertStates.Values
            .Where(state => state.Active)
            .Select(state => state.Kind)
            .ToHashSet();
        Status = new MonitorRuntimeStatus(
            attemptUtc ?? Status.LastAttemptUtc,
            error is null && _lastSnapshot is not null ? _lastSnapshot.SampledAtUtc : Status.LastSuccessUtc,
            _lastSnapshot,
            sessionValid,
            _consecutiveApiFailures,
            error,
            activeAlerts);
        StatusChanged?.Invoke(this, Status);
    }
}
