using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DwhMonitor.Core;

namespace DwhMonitor.Tests;

public sealed class DingTalkNotifierTests
{
    [Fact]
    public void BuildSignedUri_UsesDingTalkHmacSha256Formula()
    {
        const string webhook = "https://oapi.dingtalk.com/robot/send?access_token=TOKEN";
        const string secret = "SEC_TEST_SECRET";
        const long timestamp = 1786464000000;
        var expected = Convert.ToBase64String(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}\n{secret}")));

        var uri = DingTalkSigner.BuildSignedUri(webhook, secret, timestamp);

        Assert.Contains($"timestamp={timestamp}", uri.Query);
        Assert.Contains(Uri.EscapeDataString(expected), uri.Query);
        Assert.Contains("access_token=TOKEN", uri.Query);
    }

    [Fact]
    public async Task SendAsync_PostsMarkdownAndAcceptsErrCodeZero()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"errcode\":0,\"errmsg\":\"ok\"}")
        });
        using var httpClient = new HttpClient(handler);
        var notifier = new DingTalkNotifier(
            httpClient,
            () => new DingTalkCredentials(
                "https://oapi.dingtalk.com/robot/send?access_token=TOKEN",
                "SEC_TEST_SECRET"),
            new FixedClock(new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero)));

        var result = await notifier.SendAsync(new NotificationMessage("标题", "正文"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(handler.RequestUri);
        Assert.Contains("timestamp=", handler.RequestUri!.Query);
        using var payload = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("markdown", payload.RootElement.GetProperty("msgtype").GetString());
        Assert.Equal("标题", payload.RootElement.GetProperty("markdown").GetProperty("title").GetString());
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
