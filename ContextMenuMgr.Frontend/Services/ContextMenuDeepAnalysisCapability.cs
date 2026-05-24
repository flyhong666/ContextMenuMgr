using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Frontend.Services;

public static class ContextMenuDeepAnalysisCapability
{
    public static bool CanDeepAnalyze(ContextMenuEntry entry) => GetUnsupportedReason(entry) is null;

    public static bool CanDeepAnalyzeCategory(ContextMenuCategory category) => category is
        ContextMenuCategory.File
        or ContextMenuCategory.Folder
        or ContextMenuCategory.Directory
        or ContextMenuCategory.AllFileSystemObjects
        or ContextMenuCategory.DirectoryBackground
        or ContextMenuCategory.DesktopBackground;

    public static string? GetUnsupportedReason(ContextMenuEntry entry)
    {
        if (entry.EntryKind != ContextMenuEntryKind.ShellExtension)
        {
            return "OnlyShellExtensionsSupported";
        }

        if (string.IsNullOrWhiteSpace(entry.HandlerClsid))
        {
            return "MissingHandlerClsid";
        }

        if (!entry.IsPresentInRegistry)
        {
            return "MissingRegistryEntry";
        }

        if (entry.IsDeleted)
        {
            return "DeletedEntry";
        }

        if (entry.IsWindows11ContextMenu || IsPackagedComEntry(entry))
        {
            return "Windows11ModernMenuUnsupported";
        }

        if (IsSpecialMenuEntry(entry))
        {
            return "SpecialMenuUnsupported";
        }

        return CanDeepAnalyzeCategory(entry.Category)
            ? null
            : "UnsupportedCategory";
    }

    private static bool IsPackagedComEntry(ContextMenuEntry entry) =>
        ContainsRegistrySegment(entry.RegistryPath, @"PackagedCom\")
        || ContainsRegistrySegment(entry.BackendRegistryPath, @"PackagedCom\")
        || ContainsRegistrySegment(entry.SourceRootPath, @"PackagedCom\");

    private static bool IsSpecialMenuEntry(ContextMenuEntry entry) =>
        ContainsRegistrySegment(entry.RegistryPath, @"\ShellNew")
        || ContainsRegistrySegment(entry.BackendRegistryPath, @"\ShellNew")
        || ContainsRegistrySegment(entry.SourceRootPath, @"\ShellNew")
        || ContainsRegistrySegment(entry.Id, "ShellNew")
        || ContainsRegistrySegment(entry.Id, "SendTo")
        || ContainsRegistrySegment(entry.Id, "WinX");

    private static bool ContainsRegistrySegment(string? value, string segment) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(segment, StringComparison.OrdinalIgnoreCase);
}
