using System.Globalization;
using System.Windows;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Persists and restores the main window's normal placement.
/// </summary>
public sealed class MainWindowPlacementService
{
    private const string StateNormal = nameof(WindowState.Normal);
    private const string StateMaximized = nameof(WindowState.Maximized);
    private const double FallbackMinimumWidth = 100;
    private const double FallbackMinimumHeight = 100;

    private readonly FrontendSettingsService _settingsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowPlacementService"/> class.
    /// </summary>
    public MainWindowPlacementService(FrontendSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Applies saved main window placement if the saved bounds are valid for the current screen layout.
    /// </summary>
    public void ApplySavedPlacement(Window window)
    {
        var settings = _settingsService.Current;
        if (settings.MainWindowLeft is not { } left
            || settings.MainWindowTop is not { } top
            || settings.MainWindowWidth is not { } width
            || settings.MainWindowHeight is not { } height)
        {
            FrontendDebugLog.Info(
                nameof(MainWindowPlacementService),
                "MainWindowPlacementRestoreSkipped: Reason=MissingSettings");
            return;
        }

        if (!TryValidateBounds(window, left, top, width, height, out var bounds, out var reason))
        {
            FrontendDebugLog.Info(
                nameof(MainWindowPlacementService),
                $"MainWindowPlacementRestoreSkipped: Reason=InvalidBounds; Detail={reason}");
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;

        var savedState = ParseSavedState(settings.MainWindowState);
        if (savedState == WindowState.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }

        FrontendDebugLog.Info(
            nameof(MainWindowPlacementService),
            "MainWindowPlacementRestored: "
            + $"{FormatBounds(bounds)}, State={FormatState(savedState)}");
    }

    /// <summary>
    /// Saves the main window's current normal placement.
    /// </summary>
    public void SavePlacement(Window window)
    {
        var stateToSave = window.WindowState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;

        var bounds = window.WindowState switch
        {
            WindowState.Normal => new Rect(window.Left, window.Top, window.Width, window.Height),
            WindowState.Maximized => window.RestoreBounds,
            _ => window.RestoreBounds
        };

        if (!TryValidateSavableBounds(window, bounds, out var reason))
        {
            FrontendDebugLog.Info(
                nameof(MainWindowPlacementService),
                $"MainWindowPlacementSaveSkipped: Reason=InvalidBounds; Detail={reason}");
            return;
        }

        _settingsService.UpdateMainWindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            FormatState(stateToSave));

        FrontendDebugLog.Info(
            nameof(MainWindowPlacementService),
            "MainWindowPlacementSaved: "
            + $"{FormatBounds(bounds)}, State={FormatState(stateToSave)}");
    }

    private static WindowState ParseSavedState(string? state)
    {
        return string.Equals(state, StateMaximized, StringComparison.OrdinalIgnoreCase)
            ? WindowState.Maximized
            : WindowState.Normal;
    }

    private static bool TryValidateBounds(
        Window window,
        double left,
        double top,
        double width,
        double height,
        out Rect bounds,
        out string reason)
    {
        bounds = Rect.Empty;

        if (!TryValidateFiniteBounds(left, top, width, height, out reason))
        {
            return false;
        }

        var minimumWidth = GetMinimumWidth(window);
        var minimumHeight = GetMinimumHeight(window);
        if (width < minimumWidth || height < minimumHeight)
        {
            reason = string.Format(
                CultureInfo.InvariantCulture,
                "BelowMinimum Width={0:0.##}, Height={1:0.##}, MinWidth={2:0.##}, MinHeight={3:0.##}",
                width,
                height,
                minimumWidth,
                minimumHeight);
            return false;
        }

        var virtualScreen = GetVirtualScreenBounds();
        if (virtualScreen.Width > 0)
        {
            width = Math.Min(width, Math.Max(minimumWidth, virtualScreen.Width));
        }

        if (virtualScreen.Height > 0)
        {
            height = Math.Min(height, Math.Max(minimumHeight, virtualScreen.Height));
        }

        bounds = new Rect(left, top, width, height);
        if (!bounds.IntersectsWith(virtualScreen))
        {
            reason = "OutsideVirtualScreen";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateSavableBounds(Window window, Rect bounds, out string reason)
    {
        if (!TryValidateFiniteBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height, out reason))
        {
            return false;
        }

        var minimumWidth = GetMinimumWidth(window);
        var minimumHeight = GetMinimumHeight(window);
        if (bounds.Width < minimumWidth || bounds.Height < minimumHeight)
        {
            reason = string.Format(
                CultureInfo.InvariantCulture,
                "BelowMinimum Width={0:0.##}, Height={1:0.##}, MinWidth={2:0.##}, MinHeight={3:0.##}",
                bounds.Width,
                bounds.Height,
                minimumWidth,
                minimumHeight);
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateFiniteBounds(
        double left,
        double top,
        double width,
        double height,
        out string reason)
    {
        if (!IsFinite(left) || !IsFinite(top) || !IsFinite(width) || !IsFinite(height))
        {
            reason = "NonFiniteNumber";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            reason = "NonPositiveSize";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static Rect GetVirtualScreenBounds()
    {
        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private static double GetMinimumWidth(Window window)
    {
        return Math.Max(FallbackMinimumWidth, window.MinWidth);
    }

    private static double GetMinimumHeight(Window window)
    {
        return Math.Max(FallbackMinimumHeight, window.MinHeight);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string FormatState(WindowState state)
    {
        return state == WindowState.Maximized ? StateMaximized : StateNormal;
    }

    private static string FormatBounds(Rect bounds)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Left={0:0.##}, Top={1:0.##}, Width={2:0.##}, Height={3:0.##}",
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height);
    }
}
