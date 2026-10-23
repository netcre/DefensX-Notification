namespace DwhMonitor.App;

internal sealed record AppPathSet(
    string RootDirectory,
    string DataDirectory,
    string LogDirectory,
    string WebViewDirectory,
    string DatabasePath,
    string SettingsPath);

internal sealed record AppDataMigrationResult(
    int CopiedFiles,
    int SkippedFiles,
    IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count != 0;
}

internal static class AppPaths
{
    private static readonly object SyncRoot = new();
    private static readonly AppPathSet RuntimePaths = BuildForRoot(AppContext.BaseDirectory);
    private static readonly string LegacyRoot = BuildForRoot(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DwhMonitor")).RootDirectory;
    private static bool _created;
    private static AppDataMigrationResult? _lastMigrationResult;

    /// <summary>
    /// Gets the directory containing the running executable. Keeping data beside
    /// the executable makes the published directory portable and avoids relying
    /// on the process working directory during Windows auto-start.
    /// </summary>
    public static string RootDirectory => RuntimePaths.RootDirectory;
    public static string DataDirectory => RuntimePaths.DataDirectory;
    public static string LogDirectory => RuntimePaths.LogDirectory;
    public static string WebViewDirectory => RuntimePaths.WebViewDirectory;
    public static string DatabasePath => RuntimePaths.DatabasePath;
    public static string SettingsPath => RuntimePaths.SettingsPath;
    public static string LegacyRootDirectory => LegacyRoot;
    public static AppDataMigrationResult? LastMigrationResult => _lastMigrationResult;

    internal static AppPathSet BuildForRoot(string root)
    {
        var normalizedRoot = NormalizeRoot(root);
        var dataDirectory = Path.Combine(normalizedRoot, "data");
        return new AppPathSet(
            normalizedRoot,
            dataDirectory,
            Path.Combine(normalizedRoot, "logs"),
            Path.Combine(normalizedRoot, "WebView2"),
            Path.Combine(dataDirectory, "monitor.db"),
            Path.Combine(normalizedRoot, "config.json"));
    }

    /// <summary>
    /// Creates runtime directories and, once per process, copies data from the
    /// former LocalApplicationData location. Existing destination files always
    /// win; source files remain untouched so a failed migration is retryable.
    /// </summary>
    public static void EnsureCreated()
    {
        lock (SyncRoot)
        {
            if (_created)
            {
                return;
            }

            Directory.CreateDirectory(RuntimePaths.RootDirectory);
            _lastMigrationResult = MigrateLegacyRoot(LegacyRoot, RuntimePaths.RootDirectory);
            Directory.CreateDirectory(RuntimePaths.DataDirectory);
            Directory.CreateDirectory(RuntimePaths.LogDirectory);
            Directory.CreateDirectory(RuntimePaths.WebViewDirectory);
            _created = true;
        }
    }

    /// <summary>
    /// Copies the known application data entries from <paramref name="legacyRoot"/>
    /// to <paramref name="runtimeRoot"/>. The operation is deliberately
    /// non-destructive and idempotent: an existing destination file is skipped,
    /// and the legacy tree is never deleted.
    /// </summary>
    internal static AppDataMigrationResult MigrateLegacyRoot(string legacyRoot, string runtimeRoot)
    {
        var errors = new List<string>();
        var copiedFiles = 0;
        var skippedFiles = 0;

        string legacy;
        string runtime;
        try
        {
            legacy = NormalizeRoot(legacyRoot);
            runtime = NormalizeRoot(runtimeRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"路径规范化失败：{exception.Message}");
            return new AppDataMigrationResult(0, 0, errors);
        }

        if (PathsEqual(legacy, runtime))
        {
            return new AppDataMigrationResult(0, 0, errors);
        }

        if (!Directory.Exists(legacy))
        {
            return new AppDataMigrationResult(0, 0, errors);
        }

        // Do not enumerate a source tree while copying into one of its children.
        var relativeRuntime = Path.GetRelativePath(legacy, runtime);
        var runtimeIsInsideLegacy = !Path.IsPathRooted(relativeRuntime) &&
            !relativeRuntime.Equals("..", PathComparison) &&
            !relativeRuntime.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) &&
            !relativeRuntime.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison);
        if (runtimeIsInsideLegacy)
        {
            errors.Add("运行目录位于旧数据目录内部，已跳过迁移以避免递归复制。");
            return new AppDataMigrationResult(0, 0, errors);
        }

        try
        {
            Directory.CreateDirectory(runtime);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"创建运行目录失败：{exception.Message}");
            return new AppDataMigrationResult(0, 0, errors);
        }

        // Only application-owned entries are migrated; binaries and unrelated
        // files placed beside the legacy directory are not copied.
        foreach (var entryName in new[] { "config.json", "data", "logs", "WebView2" })
        {
            var source = Path.Combine(legacy, entryName);
            var destination = Path.Combine(runtime, entryName);
            if (File.Exists(source))
            {
                CopyFile(source, destination, legacy, ref copiedFiles, ref skippedFiles, errors);
            }
            else if (Directory.Exists(source))
            {
                CopyDirectory(source, destination, legacy, ref copiedFiles, ref skippedFiles, errors);
            }
        }

        return new AppDataMigrationResult(copiedFiles, skippedFiles, errors.ToArray());
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        string legacyRoot,
        ref int copiedFiles,
        ref int skippedFiles,
        ICollection<string> errors)
    {
        try
        {
            if (File.Exists(destinationDirectory))
            {
                errors.Add($"目标路径已是文件：{GetRelativePath(legacyRoot, sourceDirectory)}");
                return;
            }

            Directory.CreateDirectory(destinationDirectory);
            var attributes = File.GetAttributes(sourceDirectory);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                errors.Add($"跳过重解析目录：{GetRelativePath(legacyRoot, sourceDirectory)}");
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                CopyDirectory(
                    directory,
                    Path.Combine(destinationDirectory, name),
                    legacyRoot,
                    ref copiedFiles,
                    ref skippedFiles,
                    errors);
            }

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(name))
                {
                    CopyFile(
                        file,
                        Path.Combine(destinationDirectory, name),
                        legacyRoot,
                        ref copiedFiles,
                        ref skippedFiles,
                        errors);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"复制目录 {GetRelativePath(legacyRoot, sourceDirectory)} 失败：{exception.Message}");
        }
    }

    private static void CopyFile(
        string source,
        string destination,
        string legacyRoot,
        ref int copiedFiles,
        ref int skippedFiles,
        ICollection<string> errors)
    {
        if (File.Exists(destination))
        {
            skippedFiles++;
            return;
        }

        if (Directory.Exists(destination))
        {
            errors.Add($"目标路径已是目录：{GetRelativePath(legacyRoot, source)}");
            return;
        }

        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent))
        {
            errors.Add($"目标路径无效：{GetRelativePath(legacyRoot, source)}");
            return;
        }

        var temporary = destination + $".migration-{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(parent);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                skippedFiles++;
            }
            else
            {
                File.Move(temporary, destination);
                try
                {
                    File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
                }
                catch (IOException)
                {
                    // Timestamp preservation is best effort and does not affect
                    // the validity of the copied configuration or database.
                }

                copiedFiles++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"复制文件 {GetRelativePath(legacyRoot, source)} 失败：{exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
                // A temporary file left by a failed copy is harmless; the next
                // run uses a new name and the destination remains untouched.
            }
        }
    }

    private static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("目录路径不能为空。", nameof(root));
        }

        var fullPath = Path.GetFullPath(root);
        var pathRoot = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(pathRoot) && PathsEqual(fullPath, pathRoot))
        {
            return pathRoot;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
