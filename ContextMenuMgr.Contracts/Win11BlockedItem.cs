namespace ContextMenuMgr.Contracts;

/// <summary>
/// Represents a Win11 blocked context menu item.
/// </summary>
public sealed record Win11BlockedItem
{
    /// <summary>
    /// Gets or sets the CLSID of the blocked item.
    /// </summary>
    public string Clsid { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the blocked item.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the scope (Machine or User).
    /// </summary>
    public string Scope { get; init; } = "User";
}
