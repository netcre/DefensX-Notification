namespace DwhMonitor.App;

internal enum LoginPageRecoveryAction
{
    Wait,
    ClickExpiredTokenRelogin,
    ContractChanged
}

internal sealed record LoginPageSnapshot(
    bool Ready,
    bool TokenExpiredDialogVisible,
    int ReloginButtonCount,
    string? ContractError = null);

internal sealed record LoginPageRecoveryDecision(
    LoginPageRecoveryAction Action,
    string? Error = null);

internal static class LoginPageRecovery
{
    public static LoginPageRecoveryDecision Decide(LoginPageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.TokenExpiredDialogVisible)
        {
            return new(LoginPageRecoveryAction.Wait);
        }

        if (snapshot.ReloginButtonCount == 1)
        {
            return new(LoginPageRecoveryAction.ClickExpiredTokenRelogin);
        }

        return new(
            LoginPageRecoveryAction.ContractChanged,
            $"检测到 Token 过期弹窗，但“重新登录”按钮数量为 {snapshot.ReloginButtonCount}");
    }
}
