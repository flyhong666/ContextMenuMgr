using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Coordinates frontend theme preference, startup application, and system theme tracking.
/// </summary>
public sealed class FrontendThemeService
{
    private const WindowBackdropType BackdropType = WindowBackdropType.Mica;

    private readonly FrontendSettingsService _settingsService;
    private Window? _mainWindow;
    private Window? _watchedWindow;
    private bool _hasAppliedStartupTheme;
    private bool _watcherEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrontendThemeService"/> class.
    /// </summary>
    public FrontendThemeService(FrontendSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public event EventHandler<ThemePreferenceChangedEventArgs>? ThemePreferenceChanged;

    public AppThemeOption GetThemePreference() => _settingsService.Current.Theme;

    /// <summary>
    /// Applies the saved startup theme once and enables system tracking only for System preference.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;

        if (_hasAppliedStartupTheme)
        {
            ConfigureSystemWatcher(GetThemePreference(), mainWindow);
            return;
        }

        var preference = GetThemePreference();
        var appliedTheme = TryApplyPreference(preference, mainWindow);
        _hasAppliedStartupTheme = true;

        FrontendDebugLog.Operation(
            "ThemeStartupInitialize",
            $"SavedPreference={preference}; AppliedTheme={FormatTheme(appliedTheme)}; FollowSystem={preference == AppThemeOption.System}; WatcherEnabled={_watcherEnabled}");
    }

    /// <summary>
    /// Applies the currently saved theme preference.
    /// </summary>
    public void ApplySavedTheme(Window? mainWindow = null)
    {
        TryApplyPreference(GetThemePreference(), mainWindow ?? _mainWindow);
    }

    /// <summary>
    /// Saves and applies a new theme preference.
    /// </summary>
    public void SetThemePreference(AppThemeOption preference)
    {
        var oldPreference = GetThemePreference();
        if (oldPreference != preference)
        {
            _settingsService.UpdateTheme(preference);
        }

        var appliedTheme = TryApplyPreference(preference, _mainWindow);

        FrontendDebugLog.Operation(
            "ThemePreferenceChanged",
            $"OldPreference={oldPreference}; NewPreference={preference}; AppliedTheme={FormatTheme(appliedTheme)}");

        ThemePreferenceChanged?.Invoke(
            this,
            new ThemePreferenceChangedEventArgs(oldPreference, preference, appliedTheme));
    }

    private ApplicationTheme? TryApplyPreference(AppThemeOption preference, Window? mainWindow)
    {
        try
        {
            switch (preference)
            {
                case AppThemeOption.Light:
                    DisableSystemWatcher();
                    ApplicationThemeManager.Apply(ApplicationTheme.Light, BackdropType, updateAccent: true);
                    return ApplicationTheme.Light;
                case AppThemeOption.Dark:
                    DisableSystemWatcher();
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark, BackdropType, updateAccent: true);
                    return ApplicationTheme.Dark;
                default:
                    ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
                    ConfigureSystemWatcher(AppThemeOption.System, mainWindow);
                    return ApplicationThemeManager.GetAppTheme();
            }
        }
        catch (Exception ex)
        {
            FrontendDebugLog.Error("ThemeApplyFailed", ex, $"Preference={preference}");
            return null;
        }
    }

    private void ConfigureSystemWatcher(AppThemeOption preference, Window? mainWindow)
    {
        if (preference != AppThemeOption.System)
        {
            DisableSystemWatcher();
            return;
        }

        if (mainWindow is null)
        {
            return;
        }

        _mainWindow = mainWindow;

        if (_watcherEnabled && ReferenceEquals(_watchedWindow, mainWindow))
        {
            return;
        }

        DisableSystemWatcher();
        SystemThemeWatcher.Watch(mainWindow, BackdropType, updateAccents: true);
        _watchedWindow = mainWindow;
        _watcherEnabled = true;
    }

    private void DisableSystemWatcher()
    {
        if (!_watcherEnabled || _watchedWindow is null)
        {
            _watcherEnabled = false;
            _watchedWindow = null;
            return;
        }

        try
        {
            SystemThemeWatcher.UnWatch(_watchedWindow);
        }
        catch (Exception ex)
        {
            FrontendDebugLog.Error("ThemeApplyFailed", ex, "Failed to disable SystemThemeWatcher.");
        }

        _watcherEnabled = false;
        _watchedWindow = null;
    }

    private static string FormatTheme(ApplicationTheme? theme) => theme?.ToString() ?? "Unknown";
}

public sealed class ThemePreferenceChangedEventArgs : EventArgs
{
    public ThemePreferenceChangedEventArgs(
        AppThemeOption oldPreference,
        AppThemeOption newPreference,
        ApplicationTheme? appliedTheme)
    {
        OldPreference = oldPreference;
        NewPreference = newPreference;
        AppliedTheme = appliedTheme;
    }

    public AppThemeOption OldPreference { get; }

    public AppThemeOption NewPreference { get; }

    public ApplicationTheme? AppliedTheme { get; }
}
