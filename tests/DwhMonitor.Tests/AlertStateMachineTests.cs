using DwhMonitor.Core;

namespace DwhMonitor.Tests;

public sealed class AlertStateMachineTests
{
    private static readonly NotificationMessage AlertMessage = new("告警", "正文");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApplyCondition_NotifiesOnlyOnTransitions()
    {
        var inactive = AlertStateMachine.ApplyCondition(
            null, AlertKind.TerminalExceeded, false, Now, AlertMessage);
        Assert.False(inactive.NotificationPending);

        var active = AlertStateMachine.ApplyCondition(
            inactive, AlertKind.TerminalExceeded, true, Now.AddSeconds(1), AlertMessage);
        Assert.True(active.Active);
        Assert.True(active.NotificationPending);
        Assert.False(active.PendingRecovery);

        var sent = AlertStateMachine.MarkSent(active, Now.AddSeconds(2));
        var unchanged = AlertStateMachine.ApplyCondition(
            sent, AlertKind.TerminalExceeded, true, Now.AddSeconds(3), AlertMessage);
        Assert.Same(sent, unchanged);

        var recovered = AlertStateMachine.ApplyCondition(
            unchanged, AlertKind.TerminalExceeded, false, Now.AddSeconds(4), AlertMessage);
        Assert.False(recovered.Active);
        Assert.True(recovered.NotificationPending);
        Assert.True(recovered.PendingRecovery);
    }

    [Fact]
    public void MarkFailed_UsesThirtySecondThenCappedBackoff()
    {
        var state = AlertStateMachine.ApplyCondition(
            null, AlertKind.ApiUnavailable, true, Now, AlertMessage);

        var delays = new[] { 30, 60, 120, 300, 300 };
        foreach (var expectedSeconds in delays)
        {
            var failedAt = state.NextRetryUtc ?? Now;
            state = AlertStateMachine.MarkFailed(state, failedAt);
            Assert.Equal(failedAt.AddSeconds(expectedSeconds), state.NextRetryUtc);
        }
    }
}
