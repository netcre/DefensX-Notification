namespace DwhMonitor.App;

internal sealed record CaptchaFrame(
    string? SourceId,
    byte[]? Image,
    string? Error = null,
    AutoLoginFailureKind? FatalFailureKind = null,
    bool RequiresUserAction = false);

internal sealed record CaptchaSubmission(
    bool WasSubmitted,
    AutoLoginResult? Result,
    string? RetryError = null,
    int? ResponseCode = null)
{
    public static CaptchaSubmission Retry(string? error, int? responseCode = null) =>
        new(true, null, error, responseCode);

    public static CaptchaSubmission Complete(
        AutoLoginResult result,
        bool wasSubmitted = true,
        int? responseCode = null) => new(wasSubmitted, result, ResponseCode: responseCode);
}

internal static class CaptchaRetryCoordinator
{
    internal const int MaximumAttempts = 5;

    public static async Task<AutoLoginResult> RunAsync(
        int maximumSubmissions,
        Func<CancellationToken, Task<CaptchaFrame>> readInitialCaptchaAsync,
        Func<string?, CancellationToken, Task<CaptchaFrame>> refreshCaptchaAsync,
        Func<byte[], string?> recognize,
        Func<string, byte[], CancellationToken, Task<CaptchaSubmission>> submitAsync,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSubmissions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSubmissions, MaximumAttempts);
        ArgumentNullException.ThrowIfNull(readInitialCaptchaAsync);
        ArgumentNullException.ThrowIfNull(refreshCaptchaAsync);
        ArgumentNullException.ThrowIfNull(recognize);
        ArgumentNullException.ThrowIfNull(submitAsync);

        string? previousSourceId = null;
        string? lastError = null;
        byte[]? lastImage = null;
        string? lastPrediction = null;
        int? lastResponseCode = null;
        var submittedAttempts = 0;
        var captchaRefreshes = 0;
        var framesRead = 0;
        var uniqueImages = new HashSet<string>(StringComparer.Ordinal);
        var maximumFrames = maximumSubmissions * 3;

        while (submittedAttempts < maximumSubmissions && framesRead < maximumFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = framesRead == 0
                ? await readInitialCaptchaAsync(cancellationToken)
                : await refreshCaptchaAsync(previousSourceId, cancellationToken);
            if (framesRead > 0)
            {
                captchaRefreshes++;
            }

            framesRead++;
            if (frame.FatalFailureKind is not null)
            {
                return WithMetrics(new AutoLoginResult(
                    false,
                    frame.FatalFailureKind,
                    frame.Error,
                    frame.RequiresUserAction),
                    submittedAttempts,
                    captchaRefreshes,
                    uniqueImages.Count,
                    lastResponseCode,
                    lastImage,
                    lastPrediction);
            }

            if (string.IsNullOrWhiteSpace(frame.SourceId) || frame.Image is not { Length: > 0 })
            {
                lastError = frame.Error ?? $"第 {framesRead} 张验证码图片不可读";
                continue;
            }

            if (previousSourceId is not null &&
                string.Equals(frame.SourceId, previousSourceId, StringComparison.Ordinal))
            {
                lastError = $"第 {framesRead} 张验证码与上一张相同";
                continue;
            }

            previousSourceId = frame.SourceId;
            uniqueImages.Add(frame.SourceId);
            lastImage = frame.Image;
            var captcha = recognize(frame.Image);
            lastPrediction = captcha;
            if (captcha is null || captcha.Length != 6 || !captcha.All(char.IsDigit))
            {
                lastError = $"第 {framesRead} 张验证码识别未得到 6 位数字";
                continue;
            }

            var submission = await submitAsync(captcha, frame.Image, cancellationToken);
            if (submission.WasSubmitted)
            {
                submittedAttempts++;
            }

            lastResponseCode = submission.ResponseCode ?? lastResponseCode;
            if (submission.Result is not null)
            {
                return WithMetrics(
                    submission.Result,
                    submittedAttempts,
                    captchaRefreshes,
                    uniqueImages.Count,
                    lastResponseCode,
                    lastImage,
                    lastPrediction);
            }

            if (!submission.WasSubmitted)
            {
                return WithMetrics(new AutoLoginResult(
                    false,
                    AutoLoginFailureKind.PageContractChanged,
                    submission.RetryError ?? "登录按钮未触发",
                    true),
                    submittedAttempts,
                    captchaRefreshes,
                    uniqueImages.Count,
                    lastResponseCode,
                    lastImage,
                    lastPrediction);
            }

            lastError = submission.RetryError ?? $"第 {submittedAttempts} 次验证码未通过";
        }

        var exhaustedMessage = submittedAttempts >= maximumSubmissions
            ? $"验证码连续提交 {submittedAttempts} 次未通过"
            : $"已读取 {framesRead} 张验证码，但只有 {submittedAttempts} 次形成有效提交";
        return WithMetrics(new AutoLoginResult(
            false,
            AutoLoginFailureKind.CaptchaExhausted,
            lastError is null ? exhaustedMessage : $"{exhaustedMessage}：{lastError}",
            true),
            submittedAttempts,
            captchaRefreshes,
            uniqueImages.Count,
            lastResponseCode,
            lastImage,
            lastPrediction);
    }

    private static AutoLoginResult WithMetrics(
        AutoLoginResult result,
        int submittedAttempts,
        int captchaRefreshes,
        int uniqueImages,
        int? lastResponseCode,
        byte[]? lastImage,
        string? lastPrediction) => result with
        {
            CaptchaImage = result.CaptchaImage ?? lastImage,
            CaptchaPrediction = result.CaptchaPrediction ?? lastPrediction,
            SubmittedCaptchaAttempts = submittedAttempts,
            CaptchaRefreshes = captchaRefreshes,
            UniqueCaptchaImages = uniqueImages,
            LastResponseCode = result.LastResponseCode ?? lastResponseCode
        };
}
