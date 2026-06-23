namespace ContextMenuMgr.Contracts;

/// <summary>
/// Represents the tray Host Control Request.
/// </summary>
public sealed record TrayHostControlRequest
{
    /// <summary>
    /// Gets or sets the command.
    /// </summary>
    public TrayHostControlCommand Command { get; init; }

    /// <summary>
    /// Gets or sets the shared runtime log level.
    /// </summary>
    public RuntimeLogLevel? LogLevel { get; init; }

    /// <summary>
    /// Gets or sets whether the TrayHost notification-area icon should be visible.
    /// </summary>
    public bool? ShowTrayIcon { get; init; }
}
