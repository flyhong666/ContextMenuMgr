using ContextMenuMgr.Frontend.ViewModels;
using ContextMenuMgr.Frontend.Views;

namespace ContextMenuMgr.Frontend.Views.Pages;

/// <summary>
/// Represents the file Context Menu Page.
/// </summary>
public sealed class FileContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileContextMenuPage"/> class.
    /// </summary>
    public FileContextMenuPage(FileContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the all Objects Context Menu Page.
/// </summary>
public sealed class AllObjectsContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AllObjectsContextMenuPage"/> class.
    /// </summary>
    public AllObjectsContextMenuPage(AllObjectsContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the folder Context Menu Page.
/// </summary>
public sealed class FolderContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FolderContextMenuPage"/> class.
    /// </summary>
    public FolderContextMenuPage(FolderContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the directory Context Menu Page.
/// </summary>
public sealed class DirectoryContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryContextMenuPage"/> class.
    /// </summary>
    public DirectoryContextMenuPage(DirectoryContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the background Context Menu Page.
/// </summary>
public sealed class BackgroundContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundContextMenuPage"/> class.
    /// </summary>
    public BackgroundContextMenuPage(BackgroundContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the desktop Context Menu Page.
/// </summary>
public sealed class DesktopContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopContextMenuPage"/> class.
    /// </summary>
    public DesktopContextMenuPage(DesktopContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the drive Context Menu Page.
/// </summary>
public sealed class DriveContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DriveContextMenuPage"/> class.
    /// </summary>
    public DriveContextMenuPage(DriveContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the library Context Menu Page.
/// </summary>
public sealed class LibraryContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryContextMenuPage"/> class.
    /// </summary>
    public LibraryContextMenuPage(LibraryContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the computer Context Menu Page.
/// </summary>
public sealed class ComputerContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComputerContextMenuPage"/> class.
    /// </summary>
    public ComputerContextMenuPage(ComputerContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the recycle Bin Context Menu Page.
/// </summary>
public sealed class RecycleBinContextMenuPage : CategoryPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecycleBinContextMenuPage"/> class.
    /// </summary>
    public RecycleBinContextMenuPage(RecycleBinContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

public sealed class ApplicationGroupsPage : ApplicationGroupsPageView
{
    public ApplicationGroupsPage(ApplicationGroupsPageViewModel viewModel) => DataContext = viewModel;
}

/// <summary>
/// Represents the file Types Page.
/// </summary>
public sealed class FileTypesPage : FileTypesPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileTypesPage"/> class.
    /// </summary>
    public FileTypesPage(FileTypesPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the file type menu analysis page.
/// </summary>
public sealed class FileTypeMenuAnalysisPage : FileTypeMenuAnalysisPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileTypeMenuAnalysisPage"/> class.
    /// </summary>
    public FileTypeMenuAnalysisPage(FileTypeMenuAnalysisPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the shell new page.
/// </summary>
public sealed class ShellNewPage : SpecialMenuPageView
{
    public ShellNewPage(ShellNewPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the send to page.
/// </summary>
public sealed class SendToPage : SpecialMenuPageView
{
    public SendToPage(SendToPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the win x page.
/// </summary>
public sealed class WinXPage : SpecialMenuPageView
{
    public WinXPage(WinXPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the windows11 Context Menu Page.
/// </summary>
public sealed class Windows11ContextMenuPage : Windows11ContextMenuPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Windows11ContextMenuPage"/> class.
    /// </summary>
    public Windows11ContextMenuPage(Windows11ContextMenuPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the other Rules Page.
/// </summary>
public sealed class OtherRulesPage : OtherRulesPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OtherRulesPage"/> class.
    /// </summary>
    public OtherRulesPage(OtherRulesPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the approvals Page.
/// </summary>
public sealed class ApprovalsPage : ApprovalsPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApprovalsPage"/> class.
    /// </summary>
    public ApprovalsPage(ApprovalsPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}

/// <summary>
/// Represents the settings Page.
/// </summary>
public sealed class SettingsPage : SettingsPageView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPage"/> class.
    /// </summary>
    public SettingsPage(SettingsPageViewModel viewModel)
    {
        DataContext = viewModel;
    }
}
