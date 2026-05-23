using System.ServiceProcess;
using System.Diagnostics;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Backend.Hosting;

// The backend executable can run interactively for development, or as a
// Windows Service when installed by the frontend bootstrapper.
/// <summary>
/// Represents the backend Windows Service.
/// </summary>
public sealed class BackendWindowsService : ServiceBase
{
    private readonly BackendRuntime _runtime;
    private CancellationTokenSource? _serviceCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackendWindowsService"/> class.
    /// </summary>
    public BackendWindowsService(BackendRuntime runtime)
    {
        _runtime = runtime;
        ServiceName = ServiceMetadata.ServiceName;
        CanStop = true;
        AutoLog = true;
        CanHandleSessionChangeEvent = true;
    }

    /// <summary>
    /// Executes should Run As Service.
    /// </summary>
    public static bool ShouldRunAsService(string[] args) =>
        args.Any(static arg => string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase)) ||
        !Environment.UserInteractive;

    protected override void OnStart(string[] args)
    {
        BackendEmergencyLogger.Log($"OnStart entered. Args={string.Join(' ', args)}, PID={Environment.ProcessId}, ServiceName={ServiceName}.");
        _serviceCts = new CancellationTokenSource();
        _runtime.StopRequested += OnRuntimeStopRequested;
        BackendEmergencyLogger.Log("OnStart: scheduling StartRuntimeAsync.");
        var startupTask = StartRuntimeAsync(_serviceCts.Token);
        _ = startupTask;
        BackendEmergencyLogger.Log("OnStart: StartRuntimeAsync scheduled.");
    }

    protected override void OnStop()
    {
        BackendEmergencyLogger.Log($"OnStop entered. ServiceName={ServiceName}, PID={Environment.ProcessId}.");
        try
        {
            _runtime.StopRequested -= OnRuntimeStopRequested;
            _serviceCts?.Cancel();
            BackendEmergencyLogger.Log("OnStop: cancellation requested.");
            BackendEmergencyLogger.Log("OnStop: StopAsync started.");
            _runtime.StopAsync().GetAwaiter().GetResult();
            BackendEmergencyLogger.Log("OnStop: StopAsync completed.");
        }
        catch (Exception ex)
        {
            BackendEmergencyLogger.Log(ex, "OnStop failed.");
            throw;
        }
        finally
        {
            _serviceCts?.Dispose();
            _serviceCts = null;
        }
    }

    private void OnRuntimeStopRequested(object? sender, EventArgs e)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                BackendEmergencyLogger.Log(ex, "Runtime requested service stop, but Stop() failed.");
            }
        });
    }

    private async Task StartRuntimeAsync(CancellationToken cancellationToken)
    {
        BackendEmergencyLogger.Log($"StartRuntimeAsync entered. CancellationRequested={cancellationToken.IsCancellationRequested}.");
        try
        {
            // Keep the SCM startup path fast. The frontend/bootstrapper performs
            // the stricter "wait until pipe is ready" check separately.
            await _runtime.StartAsync(cancellationToken, ensureTrayHostOnStartup: true);
            BackendEmergencyLogger.Log("StartRuntimeAsync completed.");
        }
        catch (Exception ex)
        {
            BackendEmergencyLogger.Log(ex, "Windows service runtime startup failed.");
            BackendEmergencyLogger.Log("ServiceStartupFailedSuppressingFrontendShutdown.");
            try
            {
                _runtime.SuppressFrontendShutdownOnStop(
                    $"Windows service runtime startup failed. CancellationRequested={cancellationToken.IsCancellationRequested}");
            }
            catch (Exception suppressException)
            {
                BackendEmergencyLogger.Log(
                    suppressException,
                    "Failed to suppress frontend shutdown after service startup failure.");
            }

            try
            {
                await _runtime.LogServiceStartupFailureAsync(ex, cancellationToken.IsCancellationRequested);
            }
            catch (Exception logException)
            {
                BackendEmergencyLogger.Log(logException, "FileLogger service startup failure logging failed.");
            }

            TryWriteStartupFailureEventLog(ex, cancellationToken.IsCancellationRequested);

            try
            {
                BackendEmergencyLogger.Log("StopAfterStartupFailureRequested.");
                Stop();
            }
            catch (Exception stopException)
            {
                BackendEmergencyLogger.Log(stopException, "Stop() after Windows service runtime startup failure failed.");
            }
        }
    }

    private void TryWriteStartupFailureEventLog(Exception exception, bool cancellationRequested)
    {
        try
        {
            EventLog.WriteEntry(
                ServiceName,
                $"Windows service runtime startup failed. CancellationRequested={cancellationRequested}.{Environment.NewLine}{exception}",
                EventLogEntryType.Error);
        }
        catch
        {
        }
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        base.OnSessionChange(changeDescription);

        if (changeDescription.Reason is SessionChangeReason.SessionLogon
            or SessionChangeReason.SessionUnlock
            or SessionChangeReason.ConsoleConnect
            or SessionChangeReason.RemoteConnect)
        {
            _runtime.NotifyInteractiveSessionAvailable(changeDescription.SessionId);
        }
    }

}
