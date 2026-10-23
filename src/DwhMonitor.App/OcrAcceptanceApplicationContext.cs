using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DwhMonitor.Core;

namespace DwhMonitor.App;

internal sealed class OcrAcceptanceApplicationContext : ApplicationContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly int _requestedTrials;
    private readonly bool _captureSamples;
    private bool _started;

    public OcrAcceptanceApplicationContext(int requestedTrials, bool captureSamples)
    {
        _requestedTrials = requestedTrials;
        _captureSamples = captureSamples;
        Application.Idle += OnIdle;
    }

    private async void OnIdle(object? sender, EventArgs eventArgs)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Application.Idle -= OnIdle;
        await RunAsync();
        ExitThread();
    }

    private async Task RunAsync()
    {
        AppPaths.EnsureCreated();
        var reportPath = Path.Combine(AppPaths.DataDirectory, "ocr-acceptance.json");
        var trials = new List<OcrAcceptanceTrial>();
        var startedUtc = DateTimeOffset.UtcNow;
        string? abortReason = null;
        var modelPath = Path.Combine(AppContext.BaseDirectory, "captcha_primary_cnn.bin");
        var modelSha256 = File.Exists(modelPath)
            ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(modelPath)))
            : string.Empty;

        try
        {
            var account = new AppSettingsStore().GetActiveLoginAccount();
            if (account is null)
            {
                abortReason = "NoActiveAccount";
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(MonitorDefaults.BaseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DwhMonitor-OcrAcceptance/1.0");
            var logger = new FileMonitorLogger();
            using var recognizer = new CaptchaRecognizer(AppContext.BaseDirectory);
            for (var trialNumber = 1; trialNumber <= _requestedTrials; trialNumber++)
            {
                var sessionForm = new SessionWebViewForm(new DwhApiClient(httpClient), logger);
                AutoLoginResult? result = null;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await sessionForm.InitializeAsync();
                    await sessionForm.ClearSiteDataAsync(timeout.Token);
                    await Task.Delay(250, timeout.Token);
                    result = await sessionForm.TryAutoLoginAsync(
                        account,
                        recognizer,
                        timeout.Token,
                        maximumCaptchaAttempts: CaptchaRetryCoordinator.MaximumAttempts);
                }
                finally
                {
                    stopwatch.Stop();
                    sessionForm.Shutdown();
                    sessionForm.Dispose();
                }

                if (result is null)
                {
                    abortReason = "TrialInitializationFailed";
                    break;
                }

                var outcome = result.Success
                    ? "Success"
                    : result.FailureKind?.ToString() ?? "UnknownFailure";
                if (_captureSamples && result.CaptchaImage is not null)
                {
                    await CaptureSampleAsync(result, trialNumber);
                }

                trials.Add(new OcrAcceptanceTrial(
                    trialNumber,
                    result.Success,
                    outcome,
                    result.SubmittedCaptchaAttempts,
                    result.CaptchaRefreshes,
                    result.UniqueCaptchaImages,
                    stopwatch.ElapsedMilliseconds,
                    DateTimeOffset.UtcNow));
                await WriteReportAsync(
                    reportPath,
                    startedUtc,
                    modelSha256,
                    trials,
                    abortReason);

                if (!result.Success &&
                    result.FailureKind != AutoLoginFailureKind.CaptchaExhausted)
                {
                    abortReason = outcome;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            abortReason = "Timeout";
        }
        catch (Exception exception)
        {
            abortReason = exception.GetType().Name;
        }
        finally
        {
            await WriteReportAsync(
                reportPath,
                startedUtc,
                modelSha256,
                trials,
                abortReason);
        }
    }

    private static async Task CaptureSampleAsync(AutoLoginResult result, int trialNumber)
    {
        var root = Path.Combine(AppPaths.DataDirectory, "ocr-training-temp");
        var outcome = result.Success ? "accepted" : "rejected";
        var directory = Path.Combine(root, outcome);
        Directory.CreateDirectory(directory);
        var stem = $"sample-{trialNumber:D3}-{Guid.NewGuid():N}";
        await File.WriteAllBytesAsync(
            Path.Combine(directory, stem + ".png"),
            result.CaptchaImage!);
        if (string.IsNullOrWhiteSpace(result.CaptchaPrediction))
        {
            return;
        }

        var protectedPrediction = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(result.CaptchaPrediction),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        await File.WriteAllTextAsync(
            Path.Combine(directory, stem + ".label.dpapi"),
            Convert.ToBase64String(protectedPrediction),
            new UTF8Encoding(false));
    }

    private Task WriteReportAsync(
        string path,
        DateTimeOffset startedUtc,
        string modelSha256,
        IReadOnlyCollection<OcrAcceptanceTrial> trials,
        string? abortReason)
    {
        var successful = trials.Count(trial => trial.Success);
        var report = new OcrAcceptanceReport(
            startedUtc,
            DateTimeOffset.UtcNow,
            modelSha256,
            _requestedTrials,
            trials.Count,
            successful,
            trials.Count == 0 ? 0 : successful / (double)trials.Count,
            abortReason,
            trials);
        var json = JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
        return File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }

    private sealed record OcrAcceptanceTrial(
        int Number,
        bool Success,
        string Outcome,
        int SubmittedCaptchaAttempts,
        int CaptchaRefreshes,
        int UniqueCaptchaImages,
        long ElapsedMilliseconds,
        DateTimeOffset CompletedUtc);

    private sealed record OcrAcceptanceReport(
        DateTimeOffset StartedUtc,
        DateTimeOffset UpdatedUtc,
        string ModelSha256,
        int RequestedTrials,
        int CompletedTrials,
        int SuccessfulTrials,
        double SuccessRate,
        string? AbortReason,
        IReadOnlyCollection<OcrAcceptanceTrial> Trials);
}
