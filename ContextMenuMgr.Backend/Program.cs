using ContextMenuMgr.Backend.Hosting;

namespace ContextMenuMgr.Backend;

/// <summary>
/// Represents the program.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            BackendEmergencyLogger.Log(
                $"Program.Main started. Args={string.Join(' ', args)}, BaseDirectory={AppContext.BaseDirectory}, UserInteractive={Environment.UserInteractive}, PID={Environment.ProcessId}");

            if (BackendServiceBootstrapper.TryRun(args))
            {
                BackendEmergencyLogger.Log("Program.Main exiting after bootstrap.");
                return 0;
            }

            BackendEmergencyLogger.Log("Creating BackendRuntime.");
            using var runtime = BackendRuntime.CreateDefault();
            BackendEmergencyLogger.Log("BackendRuntime created.");

            if (BackendWindowsService.ShouldRunAsService(args))
            {
                BackendEmergencyLogger.Log("Running as Windows Service. Entering ServiceBase.Run.");
                System.ServiceProcess.ServiceBase.Run(new BackendWindowsService(runtime));
                BackendEmergencyLogger.Log("ServiceBase.Run returned.");
                return 0;
            }

            BackendEmergencyLogger.Log("Running in console mode.");
            runtime.RunConsoleAsync(args).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            BackendEmergencyLogger.Log(ex, "Fatal exception in Program.Main.");
            return 1;
        }
    }
}
