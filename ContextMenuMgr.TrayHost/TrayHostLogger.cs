using System.IO;
using System.Text;
using System.Text.Json;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.TrayHost;

/// <summary>
/// Represents the tray Host Logger.
/// </summary>
internal sealed class TrayHostLogger
{
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _logFilePath = RuntimePaths.TrayHostLogPath;
    private RuntimeLogLevel _currentLevel = RuntimeLogLevel.Warning;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayHostLogger"/> class.
    /// </summary>
    public TrayHostLogger()
    {
        PruneOldLogs();
        Configure(TryLoadPersistedLogLevel());
    }

    public void Configure(RuntimeLogLevel logLevel)
    {
        _currentLevel = logLevel;
    }

    /// <summary>
    /// Executes log Async.
    /// </summary>
    public async Task LogAsync(string message)
        => await LogAsync(RuntimeLogLevel.Information, message);

    public async Task LogAsync(RuntimeLogLevel level, string message)
    {
        if (level < _currentLevel)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(_logFilePath, line, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static RuntimeLogLevel TryLoadPersistedLogLevel()
    {
        try
        {
            if (!File.Exists(RuntimePaths.SettingsPath))
            {
                return RuntimeLogLevel.Warning;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(RuntimePaths.SettingsPath));
            if (!document.RootElement.TryGetProperty("logLevel", out var property))
            {
                return RuntimeLogLevel.Warning;
            }

            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt32(out var value) && Enum.IsDefined(typeof(RuntimeLogLevel), value)
                    => (RuntimeLogLevel)value,
                JsonValueKind.String when Enum.TryParse<RuntimeLogLevel>(property.GetString(), ignoreCase: true, out var level)
                    => level,
                _ => RuntimeLogLevel.Warning
            };
        }
        catch
        {
            return RuntimeLogLevel.Warning;
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTimeOffset.Now.Subtract(LogRetention);
            foreach (var file in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var lastWriteTime = File.GetLastWriteTimeUtc(file);
                    if (lastWriteTime < cutoff.UtcDateTime)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
