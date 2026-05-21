using System.Globalization;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Applies the same Shell Verb visibility rules used by Windows Explorer.
/// </summary>
internal static class ShellVerbVisibility
{
    private const string OpenInNewWindowRelativePath = @"Folder\shell\opennewwindow";
    private const int HideBasedOnVelocityIdDisabledValue = 0x639bc8;

    public static bool IsEnabled(RegistryKey itemKey)
    {
        if (TryGetInt32(itemKey.GetValue("HideBasedOnVelocityId"), out var velocityId)
            && velocityId == HideBasedOnVelocityIdDisabledValue)
        {
            return false;
        }

        if (itemKey.GetValue("LegacyDisable") is not null
            || itemKey.GetValue("ProgrammaticAccessOnly") is not null)
        {
            return false;
        }

        return !TryGetInt32(itemKey.GetValue("CommandFlags"), out var commandFlags)
               || commandFlags % 16 < 8;
    }

    public static void SetEnabled(RegistryKey menuKey, string registryPath, bool enable)
    {
        if (enable)
        {
            menuKey.DeleteValue("HideBasedOnVelocityId", throwOnMissingValue: false);
            DeleteHideValues(menuKey);
            return;
        }

        menuKey.SetValue("HideBasedOnVelocityId", HideBasedOnVelocityIdDisabledValue, RegistryValueKind.DWord);
        if (menuKey.GetValue("ShowAsDisabledIfHidden") is not null)
        {
            DeleteHideValues(menuKey);
            return;
        }

        menuKey.SetValue("ProgrammaticAccessOnly", string.Empty, RegistryValueKind.String);
        if (IsOpenInNewWindowVerb(registryPath))
        {
            menuKey.DeleteValue("LegacyDisable", throwOnMissingValue: false);
        }
        else
        {
            menuKey.SetValue("LegacyDisable", string.Empty, RegistryValueKind.String);
        }
    }

    private static void DeleteHideValues(RegistryKey menuKey)
    {
        menuKey.DeleteValue("LegacyDisable", throwOnMissingValue: false);
        menuKey.DeleteValue("ProgrammaticAccessOnly", throwOnMissingValue: false);
        if (TryGetInt32(menuKey.GetValue("CommandFlags"), out var commandFlags)
            && commandFlags % 16 >= 8)
        {
            menuKey.DeleteValue("CommandFlags", throwOnMissingValue: false);
        }
    }

    public static bool IsOpenInNewWindowVerb(string registryPath)
    {
        var normalizedPath = registryPath.Replace('/', '\\').TrimEnd('\\');
        return normalizedPath.EndsWith($@"\{OpenInNewWindowRelativePath}", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPath, OpenInNewWindowRelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetInt32(object? value, out int result)
    {
        try
        {
            if (value is null)
            {
                result = 0;
                return false;
            }

            result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}
