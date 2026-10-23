namespace DwhMonitor.Core;

public static class AlertStateMachine
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5)
    ];

    public static AlertState ApplyCondition(
        AlertState? current,
        AlertKind kind,
        bool active,
        DateTimeOffset now,
        NotificationMessage message)
    {
        if (current is null)
        {
            return active
                ? NewPendingState(kind, true, now, message)
                : new AlertState(kind, false, null, now, null, false, false, 0, null, null, null);
        }

        if (current.Active == active)
        {
            return current;
        }

        return NewPendingState(
            kind,
            active,
            now,
            message,
            active ? now : current.FirstSeenUtc,
            current.LastNotifiedUtc);
    }

    public static AlertState MarkSent(AlertState state, DateTimeOffset now) => state with
    {
        LastNotifiedUtc = now,
        NotificationPending = false,
        RetryCount = 0,
        NextRetryUtc = null,
        PendingTitle = null,
        PendingBody = null
    };

    public static AlertState MarkFailed(AlertState state, DateTimeOffset now)
    {
        var delayIndex = Math.Min(state.RetryCount, RetryDelays.Length - 1);
        return state with
        {
            NotificationPending = true,
            RetryCount = state.RetryCount + 1,
            NextRetryUtc = now + RetryDelays[delayIndex]
        };
    }

    private static AlertState NewPendingState(
        AlertKind kind,
        bool active,
        DateTimeOffset now,
        NotificationMessage message,
        DateTimeOffset? firstSeenUtc = null,
        DateTimeOffset? lastNotifiedUtc = null) => new(
            kind,
            active,
            active ? firstSeenUtc ?? now : firstSeenUtc,
            now,
            lastNotifiedUtc,
            true,
            !active,
            0,
            now,
            message.Title,
            message.MarkdownBody);
}
