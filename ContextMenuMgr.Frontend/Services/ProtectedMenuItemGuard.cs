using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.ViewModels;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Guards operations that can break core Windows open behavior.
/// </summary>
public static class ProtectedMenuItemGuard
{
    private const string LnkOpenGuid = "00021401-0000-0000-c000-000000000046";

    public static bool IsProtectedOpenItem(ContextMenuItemViewModel item) => IsProtectedOpenItem(item.Entry);

    public static bool IsProtectedOpenItem(ContextMenuEntry entry)
    {
        if (entry.EntryKind == ContextMenuEntryKind.ShellVerb
            && string.Equals(entry.KeyName, "open", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry.EntryKind == ContextMenuEntryKind.ShellExtension
               && IsLnkOpenGuid(entry.HandlerClsid);
    }

    public static bool IsProtectedOpenItem(SpecialMenuItemViewModel item) => IsProtectedOpenItem(item.Entry);

    public static bool IsProtectedOpenItem(SpecialMenuEntry entry)
    {
        if (entry.Kind == SpecialMenuKind.CommandStore
            && string.Equals(entry.KeyName, "open", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry.Kind == SpecialMenuKind.DragDrop
               && entry.Metadata.TryGetValue("Guid", out var guidText)
               && IsLnkOpenGuid(guidText);
    }

    public static Task<bool> ConfirmAsync(LocalizationService localization)
    {
        return FrontendMessageBox.ShowConfirmAsync(
            localization.Translate("ProtectedOpenItemPrompt"),
            localization.Translate("WindowTitle"),
            localization.Translate("DialogYes"),
            localization.Translate("DialogNo"));
    }

    private static bool IsLnkOpenGuid(string? guidText)
    {
        return Guid.TryParse(guidText, out var guid)
               && guid.Equals(Guid.Parse(LnkOpenGuid));
    }
}
