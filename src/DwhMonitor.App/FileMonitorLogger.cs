using System.Text.RegularExpressions;
using DwhMonitor.Core;

namespace DwhMonitor.App;

internal sealed partial class FileMonitorLogger : IMonitorLogger
{
    private readonly Lock _lock = new();

    public FileMonitorLogger()
    {
        CleanupOldLogs();
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var safeMessage = Redact(message);
        var exceptionText = exception is null
            ? string.Empty
            : $" | {exception.GetType().Name}: {Redact(exception.Message)}";
        var line = $"{DateTimeOffset.Now:O} [{level}] {safeMessage}{exceptionText}{Environment.NewLine}";
        lock (_lock)
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(AppPaths.LogDirectory, $"dwh-monitor-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, line, new System.Text.UTF8Encoding(false));
        }
    }

    private static string Redact(string value) =>
        DingTalkUrlRegex().Replace(value, "[DINGTALK_WEBHOOK]");

    private static void CleanupOldLogs()
    {
        if (!Directory.Exists(AppPaths.LogDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (var path in Directory.EnumerateFiles(AppPaths.LogDirectory, "dwh-monitor-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // The active log may be temporarily locked by another reader.
            }
        }
    }

    [GeneratedRegex(@"https://oapi\.dingtalk\.com/robot/send\?[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex DingTalkUrlRegex();
}
