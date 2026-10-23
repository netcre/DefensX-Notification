using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DwhMonitor.Core;

namespace DwhMonitor.App;

internal sealed record StoredLoginAccount(
    string Id,
    string ProtectedUsername,
    string ProtectedPassword,
    DateTimeOffset? LastVerifiedUtc = null);

internal sealed record StoredAppSettings(
    string? ProtectedWebhook = null,
    string? ProtectedSecret = null,
    bool AutoStartEnabled = true,
    bool AutoLoginEnabled = false,
    string? ActiveAccountId = null,
    List<StoredLoginAccount>? LoginAccounts = null);

internal sealed record LoginAccountProfile(
    string Id,
    string Username,
    string Password,
    DateTimeOffset? LastVerifiedUtc = null);

internal sealed class AppSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DwhMonitor.Settings.v2");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("DwhMonitor.Settings.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private StoredAppSettings _settings = new();

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsPath;
        Load();
    }

    public bool AutoStartEnabled => _settings.AutoStartEnabled;
    public bool AutoLoginEnabled => _settings.AutoLoginEnabled;
    public string? ActiveAccountId => _settings.ActiveAccountId;
    public bool HasCredentials => GetCredentials() is not null;
    public bool HasLoginAccount => GetLoginAccounts().Count > 0;

    public DingTalkCredentials? GetCredentials()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProtectedWebhook) ||
            string.IsNullOrWhiteSpace(_settings.ProtectedSecret))
        {
            return null;
        }

        try
        {
            return new DingTalkCredentials(
                UnprotectAny(_settings.ProtectedWebhook),
                UnprotectAny(_settings.ProtectedSecret));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public IReadOnlyList<LoginAccountProfile> GetLoginAccounts()
    {
        var accounts = new List<LoginAccountProfile>();
        foreach (var stored in _settings.LoginAccounts ?? [])
        {
            try
            {
                var username = Unprotect(stored.ProtectedUsername);
                var password = Unprotect(stored.ProtectedPassword);
                if (!string.IsNullOrWhiteSpace(stored.Id) &&
                    !string.IsNullOrWhiteSpace(username) &&
                    !string.IsNullOrWhiteSpace(password))
                {
                    accounts.Add(new LoginAccountProfile(stored.Id, username, password, stored.LastVerifiedUtc));
                }
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                // Ignore one damaged account and keep the remaining profiles usable.
            }
        }

        return accounts;
    }

    public LoginAccountProfile? GetActiveLoginAccount() =>
        GetLoginAccounts().FirstOrDefault(account => account.Id == _settings.ActiveAccountId) ??
        GetLoginAccounts().FirstOrDefault();

    public void SaveCredentials(string webhook, string secret, bool autoStartEnabled)
    {
        _settings = _settings with
        {
            ProtectedWebhook = Protect(webhook),
            ProtectedSecret = Protect(secret),
            AutoStartEnabled = autoStartEnabled
        };
        Save();
    }

    public void SaveLoginSettings(
        IReadOnlyCollection<LoginAccountProfile> accounts,
        string? activeAccountId,
        bool autoLoginEnabled)
    {
        var stored = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Id) &&
                              !string.IsNullOrWhiteSpace(account.Username) &&
                              !string.IsNullOrWhiteSpace(account.Password))
            .Select(account => new StoredLoginAccount(
                account.Id,
                Protect(account.Username),
                Protect(account.Password),
                account.LastVerifiedUtc))
            .ToList();
        var selectedId = stored.Any(account => account.Id == activeAccountId)
            ? activeAccountId
            : stored.FirstOrDefault()?.Id;
        _settings = _settings with
        {
            LoginAccounts = stored,
            ActiveAccountId = selectedId,
            AutoLoginEnabled = autoLoginEnabled && stored.Count > 0
        };
        Save();
    }

    public void MarkLoginAccountVerified(string accountId, DateTimeOffset verifiedUtc)
    {
        var accounts = GetLoginAccounts()
            .Select(account => account.Id == accountId ? account with { LastVerifiedUtc = verifiedUtc } : account)
            .ToArray();
        SaveLoginSettings(accounts, _settings.ActiveAccountId, _settings.AutoLoginEnabled);
    }

    public void SetAutoStart(bool enabled)
    {
        _settings = _settings with { AutoStartEnabled = enabled };
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            _settings = JsonSerializer.Deserialize<StoredAppSettings>(json, JsonOptions) ?? new StoredAppSettings();
        }
        catch (JsonException)
        {
            _settings = new StoredAppSettings();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _settingsPath + ".tmp";
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, _settingsPath, true);
    }

    private static string Protect(string value)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string value)
    {
        var clearBytes = ProtectedData.Unprotect(
            Convert.FromBase64String(value),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }

    private static string UnprotectAny(string value)
    {
        try
        {
            return Unprotect(value);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            var clearBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value),
                LegacyEntropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
    }
}
