using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DwhMonitor.Core;

public interface IDwhApiClient
{
    Task CheckSessionAsync(string cookieHeader, CancellationToken cancellationToken);
    Task<SessionUserInfo> GetSessionInfoAsync(string cookieHeader, CancellationToken cancellationToken);
    Task<DwhSnapshot> GetSnapshotAsync(string cookieHeader, DateTimeOffset sampledAtUtc, CancellationToken cancellationToken);
}

public sealed class SessionExpiredException(string message) : Exception(message);
public sealed class TargetNotFoundException(string message) : Exception(message);

public sealed class DwhApiClient(HttpClient httpClient, long targetGuid = MonitorDefaults.TargetGuid) : IDwhApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly long _targetGuid = targetGuid;

    public async Task CheckSessionAsync(string cookieHeader, CancellationToken cancellationToken)
    {
        await GetSessionInfoAsync(cookieHeader, cancellationToken);
    }

    public async Task<SessionUserInfo> GetSessionInfoAsync(string cookieHeader, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/user/getUserInfo", cookieHeader);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SessionExpiredException($"盾网会话失效（HTTP {(int)response.StatusCode}）");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = ParseJson(json);
        var root = document.RootElement;
        var code = GetResponseCode(root);
        if (code != 0 || !root.TryGetProperty("data", out _))
        {
            throw new SessionExpiredException(GetMessage(root, "盾网会话失效"));
        }

        return new SessionUserInfo(true, FindAccount(root));
    }

    public async Task<DwhSnapshot> GetSnapshotAsync(
        string cookieHeader,
        DateTimeOffset sampledAtUtc,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "/api/netbar/netbarlist", cookieHeader);
        request.Content = JsonContent.Create(new
        {
            page = 1,
            pageSize = 30,
            id = 0,
            preload = new[] { "NetbarGroup", "SoftPacket" },
            osearch = new { },
            asearch = new { },
            admins = new { }
        }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SessionExpiredException($"盾网会话失效（HTTP {(int)response.StatusCode}）");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSnapshotJson(json, _targetGuid, sampledAtUtc);
    }

    public static DwhSnapshot ParseSnapshotJson(string json, long targetGuid, DateTimeOffset sampledAtUtc)
    {
        using var document = ParseJson(json);
        var root = document.RootElement;
        if (GetResponseCode(root) != 0)
        {
            var message = GetMessage(root, "盾网列表接口返回失败");
            if (ContainsSessionFailure(message))
            {
                throw new SessionExpiredException(message);
            }

            throw new InvalidDataException(message);
        }

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("list", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("盾网列表响应缺少 data.list");
        }

        foreach (var row in list.EnumerateArray())
        {
            if (ReadInt64(row, "netbar_guid") != targetGuid)
            {
                continue;
            }

            return new DwhSnapshot(
                sampledAtUtc,
                targetGuid,
                ReadString(row, "netbar_name"),
                ReadBoolean(row, "status"),
                ReadInt32(row, "o"),
                ReadInt32(row, "t"),
                ReadInt32(row, "mc"),
                ReadInt32(row, "d"));
        }

        throw new TargetNotFoundException($"盾网列表中未找到 GUID {targetGuid} 的网吧");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            throw new SessionExpiredException("盾网登录 Cookie 不存在");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        request.Headers.Referrer = new Uri($"{MonitorDefaults.BaseUrl}/");
        return request;
    }

    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("盾网接口返回了无效 JSON", exception);
        }
    }

    private static int GetResponseCode(JsonElement root)
    {
        if (!root.TryGetProperty("code", out var code))
        {
            throw new InvalidDataException("盾网接口响应缺少 code");
        }

        return code.ValueKind switch
        {
            JsonValueKind.Number when code.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(code.GetString(), out var number) => number,
            _ => throw new InvalidDataException("盾网接口响应 code 格式无效")
        };
    }

    private static string GetMessage(JsonElement root, string fallback) =>
        root.TryGetProperty("msg", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString() ?? fallback
            : fallback;

    private static string? FindAccount(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            return null;
        }

        return FindAccountValue(data);
    }

    private static string? FindAccountValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("username") ||
                    property.NameEquals("userName") ||
                    property.NameEquals("account") ||
                    property.NameEquals("phone") ||
                    property.NameEquals("mobile"))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }
                }

                var nested = FindAccountValue(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindAccountValue(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool ContainsSessionFailure(string message) =>
        message.Contains("登录", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("会话", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("token", StringComparison.OrdinalIgnoreCase);

    private static int ReadInt32(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number))
        {
            return number;
        }

        throw new InvalidDataException($"盾网列表字段 {name} 缺失或格式无效");
    }

    private static long ReadInt64(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number))
        {
            return number;
        }

        throw new InvalidDataException($"盾网列表字段 {name} 缺失或格式无效");
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        throw new InvalidDataException($"盾网列表字段 {name} 缺失或格式无效");
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        throw new InvalidDataException($"盾网列表字段 {name} 缺失或格式无效");
    }
}
