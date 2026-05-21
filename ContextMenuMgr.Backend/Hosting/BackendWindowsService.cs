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
        _serviceCts = new CancellationTokenSource();
        _runtime.StopRequested += OnRuntimeStopRequested;
        _ = StartRuntimeAsync(_serviceCts.Token);
    }

    protected override void OnStop()
    {
        _runtime.StopRequested -= OnRuntimeStopRequested;
        _serviceCts?.Cancel();
        _runtime.StopAsync().GetAwaiter().GetResult();
        _serviceCts?.Dispose();
        _serviceCts = null;
    }

    private void OnRuntimeStopRequested(object? sender, EventArgs e)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Stop();
            }
            catch
            {
            }
        });
    }

    private async Task StartRuntimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Keep the SCM startup path fast. The frontend/bootstrapper performs
            // the stricter "wait until pipe is ready" check separately.
            await _runtime.StartAsync(cancellationToken, ensureTrayHostOnStartup: true);
        }
        catch (Exception ex)
        {
            await _runtime.LogServiceStartupFailureAsync(ex, cancellationToken.IsCancellationRequested);
            TryWriteStartupFailureEventLog(ex, cancellationToken.IsCancellationRequested);

            try
            {
                Stop();
            }
            catch
            {
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
