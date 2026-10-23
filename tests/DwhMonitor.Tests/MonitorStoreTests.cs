using DwhMonitor.Core;
using Microsoft.Data.Sqlite;

namespace DwhMonitor.Tests;

public sealed class MonitorStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DwhMonitorTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Store_PersistsSamplesAlertStateAndCleansOldHistory()
    {
        var store = new SqliteMonitorStore(Path.Combine(_directory, "monitor.db"));
        await store.InitializeAsync(CancellationToken.None);
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var old = CreateSnapshot(now.AddDays(-91), 900, true);
        var recent = CreateSnapshot(now, 1000, true);
        await store.InsertSampleAsync(SampleRecord.FromSnapshot(old), CancellationToken.None);
        await store.InsertSampleAsync(SampleRecord.FromSnapshot(recent), CancellationToken.None);

        var state = AlertStateMachine.ApplyCondition(
            null,
            AlertKind.TerminalExceeded,
            true,
            now,
            new NotificationMessage("标题", "正文"));
        await store.SaveAlertStateAsync(state, CancellationToken.None);

        var loadedSnapshot = await store.GetLastSuccessfulSnapshotAsync(CancellationToken.None);
        var loadedStates = await store.LoadAlertStatesAsync(CancellationToken.None);
        var deleted = await store.CleanupSamplesAsync(now.AddDays(-90), CancellationToken.None);

        Assert.Equal(recent, loadedSnapshot);
        Assert.Equal(state, loadedStates[AlertKind.TerminalExceeded]);
        Assert.Equal(1, deleted);
        Assert.True(File.Exists(Path.Combine(_directory, "monitor.db")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DwhSnapshot CreateSnapshot(DateTimeOffset time, int current, bool online) => new(
        time,
        MonitorDefaults.TargetGuid,
        "云机房",
        online,
        current,
        4293,
        1650,
        10000);
}
