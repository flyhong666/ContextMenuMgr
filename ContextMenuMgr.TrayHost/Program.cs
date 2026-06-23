using ContextMenuMgr.Contracts;
using ContextMenuMgr.TrayHost;

/// <summary>
/// Represents the program.
/// </summary>
internal static class Program
{

    [STAThread]
    private static int Main(string[] args)
    {
        var logger = new TrayHostLogger();
        try
        {
            var showTrayIcon = !args.Any(static arg => string.Equals(arg, "--hide-tray-icon", StringComparison.OrdinalIgnoreCase));
            logger.LogAsync($"TrayHost starting. ShowTrayIcon={showTrayIcon}.").GetAwaiter().GetResult();
            using var runner = new TrayHostRunner(
                new TrayBackendPipeClient(),
                new FrontendActivationService(AppContext.BaseDirectory),
                logger,
                showTrayIcon);
            var exitCode = runner.Run();
            logger.LogAsync($"TrayHost exited normally. ExitCode={exitCode}").GetAwaiter().GetResult();
            return exitCode;
        }
        catch (Exception ex)
        {
            logger.LogAsync(RuntimeLogLevel.Error, $"TrayHost crashed: {ex}").GetAwaiter().GetResult();
            return -1;
        }
    }
}
