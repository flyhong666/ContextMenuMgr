using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.Views.Pages;
using Wpf.Ui;

namespace ContextMenuMgr.Frontend.Services;

public static class RegistryProtectionDialog
{
    public static bool IsRegistryProtectionError(Exception ex)
    {
        return ex is BackendRequestException backendEx
               && string.Equals(
                   backendEx.ErrorCode,
                   PipeErrorCodes.RegistryWriteProtectionEnabled,
                   StringComparison.Ordinal);
    }

    public static async Task ShowAsync(LocalizationService localization)
    {
        await FrontendMessageBox.ShowInfoAsync(
            localization.Translate("RegistryProtectionBlocksEditMessage"),
            localization.Translate("RegistryProtectionBlocksEditTitle"),
            localization.Translate("RegistryProtectionOpenSettingsText"));

        NavigateToSettings();
    }

    public static bool ShouldBlockNormalContextMenuRegistryMutation(
        FrontendSettingsService settingsService,
        ContextMenuEntry item)
    {
        return settingsService.Current.LockNewContextMenuItems
               && IsNormalClassicContextMenuRegistryItem(item);
    }

    public static bool ShouldBlockProtectedPathMutation(
        FrontendSettingsService settingsService,
        string? path)
    {
        return settingsService.Current.LockNewContextMenuItems
               && IsProtectedNormalContextMenuRootPath(path);
    }

    private static bool IsNormalClassicContextMenuRegistryItem(ContextMenuEntry item)
    {
        if (item.IsWindows11ContextMenu)
        {
            return false;
        }

        return IsProtectedNormalContextMenuRootPath(item.BackendRegistryPath)
               || IsProtectedNormalContextMenuRootPath(item.RegistryPath)
               || IsProtectedNormalContextMenuRootPath(item.SourceRootPath);
    }

    private static bool IsProtectedNormalContextMenuRootPath(string? path)
    {
        var normalized = NormalizeProtectedPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains(@"\ShellNew\", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(@"\ShellNew", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"Shell Extensions\Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasPathSegment(normalized, "shell")
               || normalized.Contains(@"\shellex\ContextMenuHandlers", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(@"shellex\ContextMenuHandlers", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeProtectedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim().Replace('/', '\\').Trim('\\');
        foreach (var prefix in new[]
        {
            @"HKEY_CLASSES_ROOT\",
            @"HKCR\",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\",
            @"HKLM\SOFTWARE\Classes\"
        })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized[prefix.Length..].Trim('\\');
            }
        }

        foreach (var prefix in new[] { @"HKEY_USERS\", @"HKU\" })
        {
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = normalized[prefix.Length..];
            const string classesMarker = @"\Software\Classes\";
            var classesIndex = remainder.IndexOf(classesMarker, StringComparison.OrdinalIgnoreCase);
            if (classesIndex >= 0)
            {
                return remainder[(classesIndex + classesMarker.Length)..].Trim('\\');
            }
        }

        return normalized;
    }

    private static bool HasPathSegment(string path, string segment)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static void NavigateToSettings()
    {
        if (System.Windows.Application.Current is not App app)
        {
            return;
        }

        var navigationService = app.TryGetService<INavigationService>();
        navigationService?.Navigate(typeof(SettingsPage));
    }
}
