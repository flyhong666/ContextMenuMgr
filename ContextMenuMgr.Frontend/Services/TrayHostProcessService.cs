using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Represents the tray Host Process Service.
/// </summary>
public sealed class TrayHostProcessService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _trayHostExecutablePath = Path.Combine(AppContext.BaseDirectory, "ContextMenuManagerPlus.TrayHost.exe");

    /// <summary>
    /// Executes is Running.
    /// </summary>
    public bool IsRunning()
        => Process.GetProcessesByName("ContextMenuManagerPlus.TrayHost").Any();

    /// <summary>
    /// Ensures running.
    /// </summary>
    public bool EnsureRunning()
    {
        var startedAt = DateTimeOffset.UtcNow;
        FrontendDebugLog.Operation(
            "FrontendOperation",
            $"TrayHostEnsureRunningStart: Path={_trayHostExecutablePath}, Timestamp={startedAt:O}.");

        if (IsRunning())
        {
            FrontendDebugLog.Operation(
                "FrontendOperation",
                $"TrayHostEnsureRunningEnd: Success=true, AlreadyRunning=true, ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}.");
            return true;
        }

        if (!File.Exists(_trayHostExecutablePath))
        {
            FrontendDebugLog.Operation(
                "FrontendOperation",
                $"TrayHostEnsureRunningEnd: Success=false, Reason=ExecutableMissing, ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _trayHostExecutablePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_trayHostExecutablePath) ?? AppContext.BaseDirectory
            });
            FrontendDebugLog.Operation(
                "FrontendOperation",
                $"TrayHostEnsureRunningEnd: Success=true, AlreadyRunning=false, ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}.");
            return true;
        }
        catch (Exception ex)
        {
            FrontendDebugLog.Error(
                "FrontendOperation",
                ex,
                $"TrayHostEnsureRunningEnd: Success=false, Reason=StartFailed, ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}.");
            return false;
        }
    }

    /// <summary>
    /// Executes request Exit Async.
    /// </summary>
    public async Task<bool> RequestExitAsync(CancellationToken cancellationToken)
        => await SendCommandAsync(TrayHostControlCommand.Exit, cancellationToken);

    /// <summary>
    /// Executes request Reload Localization Async.
    /// </summary>
    public async Task<bool> RequestReloadLocalizationAsync(CancellationToken cancellationToken)
        => await SendCommandAsync(TrayHostControlCommand.ReloadLocalization, cancellationToken);

    public async Task<bool> SetLogLevelAsync(RuntimeLogLevel logLevel, CancellationToken cancellationToken)
        => await SendCommandAsync(
            new TrayHostControlRequest
            {
                Command = TrayHostControlCommand.SetLogLevel,
                LogLevel = logLevel
            },
            cancellationToken);

    private static async Task<bool> SendCommandAsync(TrayHostControlCommand command, CancellationToken cancellationToken)
        => await SendCommandAsync(new TrayHostControlRequest { Command = command }, cancellationToken);

    private static async Task<bool> SendCommandAsync(TrayHostControlRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        FrontendDebugLog.Operation(
            "FrontendOperation",
            $"TrayHostCommandStart: Command={request.Command}, LogLevel={request.LogLevel}, Timestamp={startedAt:O}.");

        try
        {
            using var stream = new NamedPipeClientStream(".", PipeConstants.TrayHostControlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stream.ConnectAsync(500, cancellationToken);

            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(
                request,
                JsonOptions)).WaitAsync(cancellationToken);

            var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
            if (line is null)
            {
                return false;
            }

            var response = JsonSerializer.Deserialize<TrayHostControlResponse>(line, JsonOptions);
            var success = response?.Success == true;
            FrontendDebugLog.Operation(
                "FrontendOperation",
                $"TrayHostCommandEnd: Command={request.Command}, Success={success}, Message={response?.Message}, ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}.");
            return success;
        }
        catch (Exception ex)
        {
            FrontendDebugLog.Error(
                "FrontendOperation",
                ex,
                $"TrayHostCommandEnd: Command={request.Command}, Success=false, Error={ex.GetType().Name}, ElapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0}.");
            return false;
        }
    }
}
