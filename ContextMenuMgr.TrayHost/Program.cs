using System.Runtime.InteropServices;
using ContextMenuMgr.TrayHost;

/// <summary>
/// Represents the program.
/// </summary>
internal static class Program
{

    [STAThread]
    private static int Main()
    {
        var logger = new TrayHostLogger();
        try
        {
            TrySetAppUserModelId();
            TryEnsureAppUserModelShortcut(logger);
            logger.LogAsync("TrayHost starting.").GetAwaiter().GetResult();
            using var runner = new TrayHostRunner(
                new TrayBackendPipeClient(),
                new FrontendActivationService(AppContext.BaseDirectory),
                logger);
            var exitCode = runner.Run();
            logger.LogAsync($"TrayHost exited normally. ExitCode={exitCode}").GetAwaiter().GetResult();
            return exitCode;
        }
        catch (Exception ex)
        {
            logger.LogAsync($"TrayHost crashed: {ex}").GetAwaiter().GetResult();
            return -1;
        }
    }

    private static void TrySetAppUserModelId()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppIdentity.AppUserModelId);
        }
        catch
        {
        }
    }

    private static void TryEnsureAppUserModelShortcut(TrayHostLogger logger)
    {
        if (AppUserModelIdShortcutRegistrar.TryEnsureShortcut(
                AppContext.BaseDirectory,
                out var shortcutPath,
                out var errorMessage))
        {
            logger.LogAsync($"Toast AppUserModelID shortcut ready: {shortcutPath}").GetAwaiter().GetResult();
            return;
        }

        logger.LogAsync($"Toast AppUserModelID shortcut unavailable: {errorMessage}").GetAwaiter().GetResult();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
