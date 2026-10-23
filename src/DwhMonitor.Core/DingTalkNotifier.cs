using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DwhMonitor.Core;

public static class DingTalkSigner
{
    public static Uri BuildSignedUri(string webhook, string secret, long timestampMilliseconds)
    {
        if (!Uri.TryCreate(webhook, UriKind.Absolute, out var webhookUri) || webhookUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("钉钉 Webhook 必须是有效的 HTTPS 地址", nameof(webhook));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("钉钉加签 secret 不能为空", nameof(secret));
        }

        var stringToSign = $"{timestampMilliseconds}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        var separator = string.IsNullOrEmpty(webhookUri.Query) ? "?" : "&";
        return new Uri($"{webhook}{separator}timestamp={timestampMilliseconds}&sign={Uri.EscapeDataString(signature)}");
    }
}

public sealed class DingTalkNotifier(
    HttpClient httpClient,
    Func<DingTalkCredentials?> credentialsProvider,
    IClock? clock = null) : INotificationSink
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Func<DingTalkCredentials?> _credentialsProvider = credentialsProvider;
    private readonly IClock _clock = clock ?? new SystemClock();

    public async Task<NotificationResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var credentials = _credentialsProvider();
        if (credentials is null)
        {
            return NotificationResult.Failed("钉钉机器人尚未配置");
        }

        try
        {
            var timestamp = _clock.UtcNow.ToUnixTimeMilliseconds();
            var requestUri = DingTalkSigner.BuildSignedUri(credentials.Webhook, credentials.Secret, timestamp);
            var payload = new
            {
                msgtype = "markdown",
                markdown = new
                {
                    title = message.Title,
                    text = message.MarkdownBody
                }
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return NotificationResult.Failed($"钉钉返回 HTTP {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var errorCode = root.TryGetProperty("errcode", out var code) && code.TryGetInt32(out var value)
                ? value
                : -1;
            if (errorCode != 0)
            {
                var errorMessage = root.TryGetProperty("errmsg", out var text)
                    ? text.GetString()
                    : "未知错误";
                return NotificationResult.Failed($"钉钉返回错误 {errorCode}: {errorMessage}");
            }

            return NotificationResult.Ok();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return NotificationResult.Failed($"钉钉发送失败: {exception.Message}");
        }
    }
}
