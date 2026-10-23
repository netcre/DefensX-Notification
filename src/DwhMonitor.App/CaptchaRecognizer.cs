namespace DwhMonitor.App;

internal sealed class CaptchaRecognizer : IDisposable
{
    internal const bool ReleaseAccuracyGatePassed = true;

    private readonly CaptchaPrimaryModel _model;

    public CaptchaRecognizer(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var modelPath = Path.Combine(applicationDirectory, "captcha_primary_cnn.bin");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("缺少盾网验证码专用模型。", modelPath);
        }

        _model = new CaptchaPrimaryModel(modelPath);
    }

    public string? Recognize(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        return _model.Recognize(pngBytes);
    }

    public void Dispose()
    {
    }
}
