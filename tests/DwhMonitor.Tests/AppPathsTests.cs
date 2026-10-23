using DwhMonitor.App;

namespace DwhMonitor.Tests;

/// <summary>
/// Verifies that portable runtime data is rooted beside the executable and that
/// data from the former per-user location is copied only when the new location
/// does not already contain the item.
/// </summary>
public sealed class AppPathsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"DwhMonitorAppPathsTests-{Guid.NewGuid():N}");

    public AppPathsTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void BuildForRoot_UsesRuntimeRootAndStableChildPaths()
    {
        var runtimeRoot = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var paths = AppPaths.BuildForRoot(AppContext.BaseDirectory);

        Assert.Equal(runtimeRoot, paths.RootDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "data"), paths.DataDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "logs"), paths.LogDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "WebView2"), paths.WebViewDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "data", "monitor.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(runtimeRoot, "config.json"), paths.SettingsPath);

        // The process-wide paths used by the tray app must use the same
        // executable directory rather than %LOCALAPPDATA%.
        Assert.Equal(runtimeRoot, Normalize(AppPaths.RootDirectory));
        Assert.Equal(paths.DataDirectory, AppPaths.DataDirectory);
        Assert.Equal(paths.LogDirectory, AppPaths.LogDirectory);
        Assert.Equal(paths.WebViewDirectory, AppPaths.WebViewDirectory);
        Assert.Equal(paths.DatabasePath, AppPaths.DatabasePath);
        Assert.Equal(paths.SettingsPath, AppPaths.SettingsPath);
    }

    [Fact]
    public void BuildForRoot_NormalizesRelativeAndTrailingSeparator()
    {
        var relativeRoot = Path.Combine(_directory, "runtime", ".");
        var expectedRoot = Path.GetFullPath(Path.Combine(_directory, "runtime"));

        var paths = AppPaths.BuildForRoot(relativeRoot + Path.DirectorySeparatorChar);

        Assert.Equal(expectedRoot, paths.RootDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "data"), paths.DataDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "logs"), paths.LogDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "WebView2"), paths.WebViewDirectory);
    }

    [Fact]
    public void MigrateLegacyRoot_CopiesMissingFilesAndDirectories()
    {
        var legacyRoot = Path.Combine(_directory, "legacy");
        var runtimeRoot = Path.Combine(_directory, "runtime");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "data"));
        Directory.CreateDirectory(Path.Combine(legacyRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(legacyRoot, "WebView2", "Default"));
        File.WriteAllText(Path.Combine(legacyRoot, "config.json"), "legacy-config");
        File.WriteAllText(Path.Combine(legacyRoot, "data", "monitor.db"), "legacy-db");
        File.WriteAllText(Path.Combine(legacyRoot, "logs", "dwh-monitor-old.log"), "legacy-log");
        File.WriteAllText(Path.Combine(legacyRoot, "WebView2", "Default", "Cookies"), "legacy-cookie-store");

        AppPaths.MigrateLegacyRoot(legacyRoot, runtimeRoot);

        Assert.Equal("legacy-config", File.ReadAllText(Path.Combine(runtimeRoot, "config.json")));
        Assert.Equal("legacy-db", File.ReadAllText(Path.Combine(runtimeRoot, "data", "monitor.db")));
        Assert.Equal("legacy-log", File.ReadAllText(Path.Combine(runtimeRoot, "logs", "dwh-monitor-old.log")));
        Assert.Equal(
            "legacy-cookie-store",
            File.ReadAllText(Path.Combine(runtimeRoot, "WebView2", "Default", "Cookies")));
        Assert.True(File.Exists(Path.Combine(legacyRoot, "config.json")));
        Assert.True(File.Exists(Path.Combine(legacyRoot, "data", "monitor.db")));
    }

    [Fact]
    public void MigrateLegacyRoot_DoesNotOverwriteExistingDestinationAndCopiesOtherMissingItems()
    {
        var legacyRoot = Path.Combine(_directory, "legacy");
        var runtimeRoot = Path.Combine(_directory, "runtime");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "data", "nested"));
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "data", "nested"));
        File.WriteAllText(Path.Combine(legacyRoot, "config.json"), "legacy-config");
        File.WriteAllText(Path.Combine(runtimeRoot, "config.json"), "new-config");
        File.WriteAllText(Path.Combine(legacyRoot, "data", "monitor.db"), "legacy-db");
        File.WriteAllText(Path.Combine(runtimeRoot, "data", "monitor.db"), "new-db");
        File.WriteAllText(Path.Combine(legacyRoot, "data", "nested", "missing.txt"), "copy-me");
        File.WriteAllText(Path.Combine(legacyRoot, "data", "nested", "existing.txt"), "legacy-value");
        File.WriteAllText(Path.Combine(runtimeRoot, "data", "nested", "existing.txt"), "new-value");

        AppPaths.MigrateLegacyRoot(legacyRoot, runtimeRoot);

        Assert.Equal("new-config", File.ReadAllText(Path.Combine(runtimeRoot, "config.json")));
        Assert.Equal("new-db", File.ReadAllText(Path.Combine(runtimeRoot, "data", "monitor.db")));
        Assert.Equal("copy-me", File.ReadAllText(Path.Combine(runtimeRoot, "data", "nested", "missing.txt")));
        Assert.Equal("new-value", File.ReadAllText(Path.Combine(runtimeRoot, "data", "nested", "existing.txt")));
    }

    [Fact]
    public void MigrateLegacyRoot_IsIdempotentAndLeavesSourceUntouched()
    {
        var legacyRoot = Path.Combine(_directory, "legacy");
        var runtimeRoot = Path.Combine(_directory, "runtime");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "logs"));
        File.WriteAllText(Path.Combine(legacyRoot, "logs", "old.log"), "legacy-log");

        AppPaths.MigrateLegacyRoot(legacyRoot, runtimeRoot);
        var destination = Path.Combine(runtimeRoot, "logs", "old.log");
        var firstWriteTime = File.GetLastWriteTimeUtc(destination);
        AppPaths.MigrateLegacyRoot(legacyRoot, runtimeRoot);

        Assert.Equal("legacy-log", File.ReadAllText(destination));
        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(destination));
        Assert.Equal("legacy-log", File.ReadAllText(Path.Combine(legacyRoot, "logs", "old.log")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
