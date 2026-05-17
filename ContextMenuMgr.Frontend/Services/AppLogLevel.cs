using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Defines the available app Log Level values.
/// </summary>
public enum AppLogLevel
{
    Information,
    Warning,
    Error
}

internal static class AppLogLevelExtensions
{
    public static RuntimeLogLevel ToRuntimeLogLevel(this AppLogLevel level)
        => level switch
        {
            AppLogLevel.Information => RuntimeLogLevel.Information,
            AppLogLevel.Warning => RuntimeLogLevel.Warning,
            AppLogLevel.Error => RuntimeLogLevel.Error,
            _ => RuntimeLogLevel.Warning
        };
}
