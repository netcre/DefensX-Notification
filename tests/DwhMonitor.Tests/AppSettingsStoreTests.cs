using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DwhMonitor.App;

namespace DwhMonitor.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DwhMonitorSettingsTests-{Guid.NewGuid():N}");
    private readonly string _settingsPath;

    public AppSettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _settingsPath = Path.Combine(_directory, "config.json");
    }

    [Fact]
    public void LoginAccounts_RoundTripWithDpapiAndActiveSelection()
    {
        var first = new LoginAccountProfile("first", "test-user-1", "test-password-1");
        var second = new LoginAccountProfile("second", "test-user-2", "test-password-2");
        var store = new AppSettingsStore(_settingsPath);

        store.SaveCredentials("https://oapi.dingtalk.com/robot/send?access_token=test", "test-secret", autoStartEnabled: false);
        store.SaveLoginSettings([first, second], second.Id, autoLoginEnabled: true);

        var reloaded = new AppSettingsStore(_settingsPath);
        Assert.True(reloaded.AutoLoginEnabled);
        Assert.Equal(second, reloaded.GetActiveLoginAccount());
        Assert.Equal([first, second], reloaded.GetLoginAccounts());

        var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
        Assert.DoesNotContain(first.Username, json, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Password, json, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Username, json, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Password, json, StringComparison.Ordinal);
        Assert.DoesNotContain("test-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAccounts_ActiveIdFallsBackAndRemovingAccountPersists()
    {
        var first = new LoginAccountProfile("first", "test-user-1", "test-password-1");
        var second = new LoginAccountProfile("second", "test-user-2", "test-password-2");
        var store = new AppSettingsStore(_settingsPath);

        store.SaveLoginSettings([first, second], "missing", autoLoginEnabled: true);
        Assert.Equal(first, store.GetActiveLoginAccount());

        store.SaveLoginSettings([second], second.Id, autoLoginEnabled: true);
        var reloaded = new AppSettingsStore(_settingsPath);
        Assert.Single(reloaded.GetLoginAccounts());
        Assert.Equal(second, reloaded.GetActiveLoginAccount());
        Assert.True(reloaded.AutoLoginEnabled);

        store.SaveLoginSettings([], null, autoLoginEnabled: true);
        Assert.Empty(new AppSettingsStore(_settingsPath).GetLoginAccounts());
        Assert.False(new AppSettingsStore(_settingsPath).AutoLoginEnabled);
    }

    [Fact]
    public void DamagedCiphertext_IsSkippedWithoutAffectingOtherAccounts()
    {
        var valid = new LoginAccountProfile("valid", "test-valid", "test-password");
        var store = new AppSettingsStore(_settingsPath);
        store.SaveLoginSettings([valid], valid.Id, autoLoginEnabled: true);

        using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath, Encoding.UTF8));
        var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(document.RootElement.GetRawText())!;
        root["LoginAccounts"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = "damaged",
                ["ProtectedUsername"] = "not-base64",
                ["ProtectedPassword"] = "not-base64"
            },
            new Dictionary<string, object?>
            {
                ["Id"] = valid.Id,
                ["ProtectedUsername"] = ReadStoredProperty(document, "ProtectedUsername"),
                ["ProtectedPassword"] = ReadStoredProperty(document, "ProtectedPassword")
            }
        };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(root), new UTF8Encoding(false));

        var reloaded = new AppSettingsStore(_settingsPath);
        var accounts = reloaded.GetLoginAccounts();
        Assert.Single(accounts);
        Assert.Equal(valid, accounts[0]);
    }

    [Fact]
    public void LegacyDingTalkCiphertext_IsStillReadableAndMigratesToEmptyLoginList()
    {
        var legacyEntropy = Encoding.UTF8.GetBytes("DwhMonitor.Settings.v1");
        static string Protect(string value, byte[] entropy) => Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser));
        var json = JsonSerializer.Serialize(new
        {
            ProtectedWebhook = Protect("https://oapi.dingtalk.com/robot/send?access_token=legacy", legacyEntropy),
            ProtectedSecret = Protect("legacy-secret", legacyEntropy),
            AutoStartEnabled = true
        });
        File.WriteAllText(_settingsPath, json, new UTF8Encoding(false));

        var store = new AppSettingsStore(_settingsPath);
        Assert.Equal("legacy-secret", store.GetCredentials()?.Secret);
        Assert.Empty(store.GetLoginAccounts());
        Assert.False(store.AutoLoginEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string ReadStoredProperty(JsonDocument document, string name) =>
        document.RootElement.GetProperty("LoginAccounts")[0].GetProperty(name).GetString()!;
}
