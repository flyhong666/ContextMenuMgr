using System.IO;
using System.Security.Principal;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Backend.Hosting;

internal static class BackendEmergencyLogger
{
    private static readonly Lock SyncRoot = new();

    public static void Log(string message)
    {
        try
        {
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {FormatMessage(message)}{Environment.NewLine}";
            var logPath = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());
            lock (SyncRoot)
            {
                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
        }
    }

    public static void Log(Exception exception, string context)
        => Log($"{context}{Environment.NewLine}{exception}");

    private static string FormatMessage(string message)
        => $"PID={Environment.ProcessId}, BaseDirectory={AppContext.BaseDirectory}, UserInteractive={Environment.UserInteractive}, Identity={TryGetCurrentIdentityName()}. {message}";

    private static string GetLogPath()
    {
        try
        {
            return Path.Combine(RuntimePaths.DataDirectory, "Logs", "service-startup.log");
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ContextMenuMgr",
                "Logs",
                "service-startup.log");
        }
    }

    private static string TryGetCurrentIdentityName()
    {
        try
        {
            return WindowsIdentity.GetCurrent().Name;
        }
        catch (Exception ex)
        {
            return $"<unavailable:{ex.GetType().Name}>";
        }
    }
}
