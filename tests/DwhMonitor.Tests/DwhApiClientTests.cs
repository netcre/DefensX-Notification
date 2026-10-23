using DwhMonitor.Core;
using System.Net;

namespace DwhMonitor.Tests;

public sealed class DwhApiClientTests
{
    private static readonly DateTimeOffset SampleTime = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseSnapshotJson_MapsVerifiedFieldsAndMatchesLongGuid()
    {
        var json = """
            {
              "code": 0,
              "data": {
                "list": [
                  {"netbar_guid": 1, "netbar_name": "其他", "status": false, "o": 1, "t": 2, "mc": 3, "d": 4},
                  {"netbar_guid": 2547092390, "netbar_name": "云机房", "status": true, "o": 998, "t": 4293, "mc": 1650, "d": 10528}
                ]
              },
              "msg": "查询成功"
            }
            """;

        var snapshot = DwhApiClient.ParseSnapshotJson(json, MonitorDefaults.TargetGuid, SampleTime);

        Assert.Equal(MonitorDefaults.TargetGuid, snapshot.TargetGuid);
        Assert.Equal("云机房", snapshot.NetbarName);
        Assert.True(snapshot.Online);
        Assert.Equal(998, snapshot.TerminalCurrent);
        Assert.Equal(4293, snapshot.TerminalTotal);
        Assert.Equal(1650, snapshot.VipLimit);
        Assert.Equal(10528, snapshot.DefenseCount);
    }

    [Fact]
    public void ParseSnapshotJson_ThrowsWhenTargetIsMissing()
    {
        var json = """{"code":0,"data":{"list":[]},"msg":"查询成功"}""";

        var exception = Assert.Throws<TargetNotFoundException>(() =>
            DwhApiClient.ParseSnapshotJson(json, MonitorDefaults.TargetGuid, SampleTime));

        Assert.Contains(MonitorDefaults.TargetGuid.ToString(), exception.Message);
    }

    [Fact]
    public void ParseSnapshotJson_RejectsInvalidJson()
    {
        Assert.Throws<InvalidDataException>(() =>
            DwhApiClient.ParseSnapshotJson("not-json", MonitorDefaults.TargetGuid, SampleTime));
    }

    [Fact]
    public async Task GetSessionInfoAsync_ExtractsNestedAccount()
    {
        var handler = new StubHttpMessageHandler("""
            {"code":0,"data":{"userinfo":{"username":"17700000000"}}}
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(MonitorDefaults.BaseUrl) };
        var api = new DwhApiClient(client);

        var info = await api.GetSessionInfoAsync("session=test", CancellationToken.None);

        Assert.True(info.IsValid);
        Assert.Equal("17700000000", info.Account);
    }

    private sealed class StubHttpMessageHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
