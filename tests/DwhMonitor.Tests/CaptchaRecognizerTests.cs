using DwhMonitor.App;

namespace DwhMonitor.Tests;

public sealed class CaptchaRecognizerTests
{
    private static readonly Lazy<string?[]> RealCaptchaPredictions = new(RecognizeRealCaptchaSet);

    private static readonly string[] ExpectedCodes =
    [
        "856779", "752522", "983095", "930762", "800095",
        "449369", "189292", "693493", "605687", "959365",
        "187456", "372502", "646671", "816974", "205018",
        "068330", "989121", "677186", "874800", "787350",
        "290744", "213314", "978997", "059773", "342629",
        "324204", "842425", "937532", "212426", "298005",
        "980450", "854243", "366325", "534334", "792036",
        "133716", "605343", "627508", "213402", "562238",
        "174572", "920847", "060470", "322849", "342814",
        "110724", "698236", "041803", "048237", "654652"
    ];

    [Fact]
    public void RealCaptchaSet_UsesStrictSixDigitContract()
    {
        for (var index = 0; index < ExpectedCodes.Length; index++)
        {
            var actual = RealCaptchaPredictions.Value[index];
            Assert.True(actual is null || actual.Length == 6 && actual.All(char.IsDigit),
                $"样本 {index + 1:D2} 返回了非六位数字结果：{actual}");
        }
    }

    [Fact]
    public void RealCaptchaSet_FiveIndependentRefreshesMeetCombinedSuccessFloor()
    {
        var correct = 0;
        var failures = new List<string>();

        for (var index = 0; index < ExpectedCodes.Length; index++)
        {
            var actual = RealCaptchaPredictions.Value[index];
            if (actual == ExpectedCodes[index])
            {
                correct++;
            }
            else
            {
                failures.Add($"{index + 1:D2}: expected={ExpectedCodes[index]}, actual={actual ?? "<null>"}");
            }
        }

        var singleAttemptSuccessRate = correct / (double)ExpectedCodes.Length;
        var fiveAttemptSuccessRate = 1d - Math.Pow(1d - singleAttemptSuccessRate, 5);
        Assert.True(
            fiveAttemptSuccessRate >= 0.99d,
            $"单次验证码整串识别率 {correct}/50，五次独立刷新组合成功率 {fiveAttemptSuccessRate:P3}。" +
            string.Join("; ", failures));
    }

    [Fact]
    public void ReleaseAutoLogin_UsesAtMostFiveCaptchaSubmissions()
    {
        Assert.True(CaptchaRecognizer.ReleaseAccuracyGatePassed);
        Assert.Equal(5, CaptchaRetryCoordinator.MaximumAttempts);
    }

    [Fact]
    public void BlankImage_IsNotAcceptedAsCaptcha()
    {
        using var bitmap = new Bitmap(240, 80);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        using var recognizer = new CaptchaRecognizer(AppContext.BaseDirectory);

        Assert.Null(recognizer.Recognize(stream.ToArray()));
    }

    private static string?[] RecognizeRealCaptchaSet()
    {
        using var recognizer = new CaptchaRecognizer(AppContext.BaseDirectory);
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Captcha");
        return Enumerable.Range(1, ExpectedCodes.Length)
            .Select(index => Path.Combine(fixtureDirectory, $"sample-{index:D2}.png"))
            .Select(path => recognizer.Recognize(File.ReadAllBytes(path)))
            .ToArray();
    }
}
