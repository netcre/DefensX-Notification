using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DwhMonitor.Core;

public interface IMonitorStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task InsertSampleAsync(SampleRecord sample, CancellationToken cancellationToken);
    Task<DwhSnapshot?> GetLastSuccessfulSnapshotAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<AlertKind, AlertState>> LoadAlertStatesAsync(CancellationToken cancellationToken);
    Task SaveAlertStateAsync(AlertState state, CancellationToken cancellationToken);
    Task<int> CleanupSamplesAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}

public sealed class SqliteMonitorStore : IMonitorStore
{
    private readonly string _connectionString;

    public SqliteMonitorStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sampled_at_utc TEXT NOT NULL,
                target_guid INTEGER NOT NULL,
                netbar_name TEXT NULL,
                online INTEGER NULL,
                terminal_current INTEGER NULL,
                terminal_total INTEGER NULL,
                vip_limit INTEGER NULL,
                defense_count INTEGER NULL,
                auth_ok INTEGER NULL,
                error_kind TEXT NULL,
                error_message TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_samples_sampled_at
                ON samples(sampled_at_utc);

            CREATE TABLE IF NOT EXISTS alert_state (
                kind TEXT PRIMARY KEY,
                active INTEGER NOT NULL,
                first_seen_utc TEXT NULL,
                last_change_utc TEXT NOT NULL,
                last_notified_utc TEXT NULL,
                notification_pending INTEGER NOT NULL,
                pending_recovery INTEGER NOT NULL,
                retry_count INTEGER NOT NULL,
                next_retry_utc TEXT NULL,
                pending_title TEXT NULL,
                pending_body TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertSampleAsync(SampleRecord sample, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO samples (
                sampled_at_utc, target_guid, netbar_name, online,
                terminal_current, terminal_total, vip_limit, defense_count,
                auth_ok, error_kind, error_message)
            VALUES (
                $sampled_at_utc, $target_guid, $netbar_name, $online,
                $terminal_current, $terminal_total, $vip_limit, $defense_count,
                $auth_ok, $error_kind, $error_message);
            """;
        command.Parameters.AddWithValue("$sampled_at_utc", Format(sample.SampledAtUtc));
        command.Parameters.AddWithValue("$target_guid", sample.TargetGuid);
        command.Parameters.AddWithValue("$netbar_name", DbValue(sample.NetbarName));
        command.Parameters.AddWithValue("$online", DbValue(sample.Online));
        command.Parameters.AddWithValue("$terminal_current", DbValue(sample.TerminalCurrent));
        command.Parameters.AddWithValue("$terminal_total", DbValue(sample.TerminalTotal));
        command.Parameters.AddWithValue("$vip_limit", DbValue(sample.VipLimit));
        command.Parameters.AddWithValue("$defense_count", DbValue(sample.DefenseCount));
        command.Parameters.AddWithValue("$auth_ok", DbValue(sample.AuthOk));
        command.Parameters.AddWithValue("$error_kind", DbValue(sample.ErrorKind));
        command.Parameters.AddWithValue("$error_message", DbValue(TrimError(sample.ErrorMessage)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DwhSnapshot?> GetLastSuccessfulSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sampled_at_utc, target_guid, netbar_name, online,
                   terminal_current, terminal_total, vip_limit, defense_count
            FROM samples
            WHERE error_kind IS NULL
            ORDER BY id DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DwhSnapshot(
            Parse(reader.GetString(0)),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt64(3) != 0,
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7));
    }

    public async Task<IReadOnlyDictionary<AlertKind, AlertState>> LoadAlertStatesAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<AlertKind, AlertState>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, active, first_seen_utc, last_change_utc, last_notified_utc,
                   notification_pending, pending_recovery, retry_count,
                   next_retry_utc, pending_title, pending_body
            FROM alert_state;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<AlertKind>(reader.GetString(0), out var kind))
            {
                continue;
            }

            result[kind] = new AlertState(
                kind,
                reader.GetInt64(1) != 0,
                ReadDateTime(reader, 2),
                Parse(reader.GetString(3)),
                ReadDateTime(reader, 4),
                reader.GetInt64(5) != 0,
                reader.GetInt64(6) != 0,
                reader.GetInt32(7),
                ReadDateTime(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10));
        }

        return result;
    }

    public async Task SaveAlertStateAsync(AlertState state, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO alert_state (
                kind, active, first_seen_utc, last_change_utc, last_notified_utc,
                notification_pending, pending_recovery, retry_count,
                next_retry_utc, pending_title, pending_body)
            VALUES (
                $kind, $active, $first_seen_utc, $last_change_utc, $last_notified_utc,
                $notification_pending, $pending_recovery, $retry_count,
                $next_retry_utc, $pending_title, $pending_body)
            ON CONFLICT(kind) DO UPDATE SET
                active = excluded.active,
                first_seen_utc = excluded.first_seen_utc,
                last_change_utc = excluded.last_change_utc,
                last_notified_utc = excluded.last_notified_utc,
                notification_pending = excluded.notification_pending,
                pending_recovery = excluded.pending_recovery,
                retry_count = excluded.retry_count,
                next_retry_utc = excluded.next_retry_utc,
                pending_title = excluded.pending_title,
                pending_body = excluded.pending_body;
            """;
        command.Parameters.AddWithValue("$kind", state.Kind.ToString());
        command.Parameters.AddWithValue("$active", state.Active ? 1 : 0);
        command.Parameters.AddWithValue("$first_seen_utc", DbValue(Format(state.FirstSeenUtc)));
        command.Parameters.AddWithValue("$last_change_utc", Format(state.LastChangeUtc));
        command.Parameters.AddWithValue("$last_notified_utc", DbValue(Format(state.LastNotifiedUtc)));
        command.Parameters.AddWithValue("$notification_pending", state.NotificationPending ? 1 : 0);
        command.Parameters.AddWithValue("$pending_recovery", state.PendingRecovery ? 1 : 0);
        command.Parameters.AddWithValue("$retry_count", state.RetryCount);
        command.Parameters.AddWithValue("$next_retry_utc", DbValue(Format(state.NextRetryUtc)));
        command.Parameters.AddWithValue("$pending_title", DbValue(state.PendingTitle));
        command.Parameters.AddWithValue("$pending_body", DbValue(state.PendingBody));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CleanupSamplesAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM samples WHERE sampled_at_utc < $cutoff_utc;";
        command.Parameters.AddWithValue("$cutoff_utc", Format(cutoffUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;
    private static object DbValue(int? value) => value.HasValue ? value.Value : DBNull.Value;
    private static object DbValue(bool? value) => value.HasValue ? value.Value ? 1 : 0 : DBNull.Value;
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? Format(DateTimeOffset? value) => value.HasValue ? Format(value.Value) : null;
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset? ReadDateTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));

    private static string? TrimError(string? value) =>
        value is { Length: > 1000 } ? value[..1000] : value;
}
