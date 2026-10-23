using DwhMonitor.App;

namespace DwhMonitor.Tests;

public sealed class LoginPageRecoveryTests
{
    [Fact]
    public void ExpiredTokenDialog_WithOneReloginButtonRequestsClick()
    {
        var decision = LoginPageRecovery.Decide(new LoginPageSnapshot(
            Ready: false,
            TokenExpiredDialogVisible: true,
            ReloginButtonCount: 1));

        Assert.Equal(LoginPageRecoveryAction.ClickExpiredTokenRelogin, decision.Action);
    }

    [Fact]
    public void ExpiredTokenDialog_WithMissingOrDuplicateButtonStopsAsContractChange()
    {
        foreach (var count in new[] { 0, 2 })
        {
            var decision = LoginPageRecovery.Decide(new LoginPageSnapshot(
                Ready: false,
                TokenExpiredDialogVisible: true,
                ReloginButtonCount: count));

            Assert.Equal(LoginPageRecoveryAction.ContractChanged, decision.Action);
            Assert.Contains("重新登录", decision.Error);
        }
    }

    [Fact]
    public void ReadyLoginContractDoesNotTriggerRecovery()
    {
        var decision = LoginPageRecovery.Decide(new LoginPageSnapshot(
            Ready: true,
            TokenExpiredDialogVisible: false,
            ReloginButtonCount: 0));

        Assert.Equal(LoginPageRecoveryAction.Wait, decision.Action);
    }
}
