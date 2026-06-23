using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Represents the tray Host Control Client.
/// </summary>
public static class TrayHostControlClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Attempts to send Exit Async.
    /// </summary>
    public static async Task<bool> TrySendExitAsync(CancellationToken cancellationToken)
        => await TrySendCommandAsync(TrayHostControlCommand.Exit, cancellationToken);

    /// <summary>
    /// Attempts to reload Localization Async.
    /// </summary>
    public static async Task<bool> TryReloadLocalizationAsync(CancellationToken cancellationToken)
        => await TrySendCommandAsync(TrayHostControlCommand.ReloadLocalization, cancellationToken);

    public static async Task<bool> TrySetLogLevelAsync(RuntimeLogLevel logLevel, CancellationToken cancellationToken)
        => await TrySendRequestAsync(
            new TrayHostControlRequest
            {
                Command = TrayHostControlCommand.SetLogLevel,
                LogLevel = logLevel
            },
            cancellationToken);

    public static async Task<bool> TrySetTrayIconVisibleAsync(bool visible, CancellationToken cancellationToken)
        => await TrySendRequestAsync(
            new TrayHostControlRequest
            {
                Command = TrayHostControlCommand.SetTrayIconVisibility,
                ShowTrayIcon = visible
            },
            cancellationToken);

    private static async Task<bool> TrySendCommandAsync(TrayHostControlCommand command, CancellationToken cancellationToken)
        => await TrySendRequestAsync(new TrayHostControlRequest { Command = command }, cancellationToken);

    private static async Task<bool> TrySendRequestAsync(TrayHostControlRequest request, CancellationToken cancellationToken)
    {
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
            return response?.Success == true;
        }
        catch
        {
            return false;
        }
    }
}
