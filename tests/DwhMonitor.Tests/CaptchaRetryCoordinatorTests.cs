using DwhMonitor.App;

namespace DwhMonitor.Tests;

public sealed class CaptchaRetryCoordinatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task SuccessOnAttempt_StopsAtThatAttempt(int successAttempt)
    {
        var acquired = 0;
        var refreshes = 0;
        var submissions = 0;

        var result = await CaptchaRetryCoordinator.RunAsync(
            CaptchaRetryCoordinator.MaximumAttempts,
            _ => Task.FromResult(Frame(++acquired)),
            (previousSource, _) =>
            {
                refreshes++;
                Assert.Equal($"captcha-{acquired}", previousSource);
                return Task.FromResult(Frame(++acquired));
            },
            _ => "123456",
            (_, _, _) =>
            {
                submissions++;
                return Task.FromResult(submissions == successAttempt
                    ? CaptchaSubmission.Complete(new AutoLoginResult(true))
                    : CaptchaSubmission.Retry("验证码错误"));
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(successAttempt, acquired);
        Assert.Equal(successAttempt - 1, refreshes);
        Assert.Equal(successAttempt, submissions);
        Assert.Equal(successAttempt, result.SubmittedCaptchaAttempts);
        Assert.Equal(successAttempt, result.UniqueCaptchaImages);
    }

    [Fact]
    public async Task FiveCaptchaFailures_StopWithoutSixthAttempt()
    {
        var acquired = 0;
        var refreshes = 0;
        var submissions = 0;

        var result = await CaptchaRetryCoordinator.RunAsync(
            CaptchaRetryCoordinator.MaximumAttempts,
            _ => Task.FromResult(Frame(++acquired)),
            (_, _) =>
            {
                refreshes++;
                return Task.FromResult(Frame(++acquired));
            },
            _ => "123456",
            (_, _, _) =>
            {
                submissions++;
                return Task.FromResult(CaptchaSubmission.Retry("验证码错误"));
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AutoLoginFailureKind.CaptchaExhausted, result.FailureKind);
        Assert.Equal(5, acquired);
        Assert.Equal(4, refreshes);
        Assert.Equal(5, submissions);
        Assert.Equal(5, result.SubmittedCaptchaAttempts);
        Assert.Equal(5, result.UniqueCaptchaImages);
    }

    [Fact]
    public async Task LowConfidencePredictions_DoNotConsumeSubmissionLimit()
    {
        var acquired = 0;
        var submissions = 0;

        var result = await CaptchaRetryCoordinator.RunAsync(
            maximumSubmissions: 2,
            _ => Task.FromResult(Frame(++acquired)),
            (_, _) => Task.FromResult(Frame(++acquired)),
            _ => acquired <= 4 ? null : "123456",
            (_, _, _) =>
            {
                submissions++;
                return Task.FromResult(submissions == 2
                    ? CaptchaSubmission.Complete(new AutoLoginResult(true))
                    : CaptchaSubmission.Retry("验证码错误"));
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(6, acquired);
        Assert.Equal(2, submissions);
        Assert.Equal(2, result.SubmittedCaptchaAttempts);
        Assert.Equal(6, result.UniqueCaptchaImages);
    }

    [Fact]
    public async Task Retry_RequiresAChangedCaptchaImageBeforeSubmission()
    {
        var acquired = 1;
        var refreshes = 0;
        var submissions = 0;

        var result = await CaptchaRetryCoordinator.RunAsync(
            maximumSubmissions: 3,
            _ => Task.FromResult(Frame(acquired)),
            (previousSource, _) =>
            {
                refreshes++;
                if (refreshes == 1)
                {
                    return Task.FromResult(Frame(acquired));
                }

                Assert.Equal($"captcha-{acquired}", previousSource);
                return Task.FromResult(Frame(++acquired));
            },
            _ => "123456",
            (_, _, _) =>
            {
                submissions++;
                return Task.FromResult(submissions == 2
                    ? CaptchaSubmission.Complete(new AutoLoginResult(true))
                    : CaptchaSubmission.Retry("验证码错误"));
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, refreshes);
        Assert.Equal(2, submissions);
        Assert.Equal(2, result.SubmittedCaptchaAttempts);
        Assert.Equal(2, result.UniqueCaptchaImages);
    }

    [Fact]
    public async Task FatalRefreshFailure_DoesNotConsumeAnotherSubmissionAttempt()
    {
        var submissions = 0;
        var result = await CaptchaRetryCoordinator.RunAsync(
            maximumSubmissions: 5,
            _ => Task.FromResult(Frame(1)),
            (_, _) => Task.FromResult(new CaptchaFrame(
                null,
                null,
                "刷新控件缺失",
                AutoLoginFailureKind.PageContractChanged,
                true)),
            _ => "123456",
            (_, _, _) =>
            {
                submissions++;
                return Task.FromResult(CaptchaSubmission.Retry("验证码错误"));
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AutoLoginFailureKind.PageContractChanged, result.FailureKind);
        Assert.Equal(1, submissions);
        Assert.Equal(1, result.SubmittedCaptchaAttempts);
        Assert.Equal(1, result.UniqueCaptchaImages);
    }

    private static CaptchaFrame Frame(int sequence) =>
        new($"captcha-{sequence}", [(byte)sequence]);
}
