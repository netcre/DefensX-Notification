namespace DwhMonitor.App;

internal enum AutoLoginFailureKind
{
    CredentialRejected,
    CaptchaExhausted,
    NetworkUnavailable,
    SmsRequired,
    PageContractChanged,
    UnexpectedResponse
}

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
