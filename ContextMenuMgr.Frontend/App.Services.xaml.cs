using ContextMenuMgr.Frontend.Services;
using ContextMenuMgr.Frontend.ViewModels;
using ContextMenuMgr.Frontend.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace ContextMenuMgr.Frontend;

/// <summary>
/// Represents the app.
/// </summary>
public partial class App
{
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddNavigationViewPageProvider();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<FrontendSettingsService>();
        services.AddSingleton<FrontendStartupService>();
        services.AddSingleton<TrayHostProcessService>();
        services.AddSingleton<FrontendNavigationState>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<Services.ThemeService>();
        services.AddSingleton<IconPreviewService>();
        services.AddSingleton<RuleDictionaryCatalogService>();
        services.AddSingleton<EnhanceMenuRuleService>();
        services.AddSingleton<DetailedEditRuleService>();
        services.AddSingleton<Windows11ContextMenuService>();
        services.AddSingleton<ContextMenuItemActionsService>();
        services.AddSingleton<IBackendClient, NamedPipeBackendClient>();
        services.AddSingleton<IBackendServiceManager, BackendServiceManager>();
        services.AddSingleton<ContextMenuWorkspaceService>();
        services.AddSingleton<ExplorerRestartStateService>();
        services.AddSingleton<IInfoBarService, InfoBarService>();
        services.AddSingleton<UpdateCheckService>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<ShellViewModel>(),
            sp,
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IInfoBarService>(),
            sp.GetRequiredService<UpdateCheckService>()));

        services.AddSingleton<FileContextMenuPageViewModel>();
        services.AddSingleton<AllObjectsContextMenuPageViewModel>();
        services.AddSingleton<FolderContextMenuPageViewModel>();
        services.AddSingleton<DirectoryContextMenuPageViewModel>();
        services.AddSingleton<BackgroundContextMenuPageViewModel>();
        services.AddSingleton<DesktopContextMenuPageViewModel>();
        services.AddSingleton<DriveContextMenuPageViewModel>();
        services.AddSingleton<LibraryContextMenuPageViewModel>();
        services.AddSingleton<ComputerContextMenuPageViewModel>();
        services.AddSingleton<RecycleBinContextMenuPageViewModel>();
        services.AddSingleton<FileTypesPageViewModel>();
        services.AddSingleton<ShellNewPageViewModel>();
        services.AddSingleton<SendToPageViewModel>();
        services.AddSingleton<WinXPageViewModel>();
        services.AddSingleton<OtherRulesPageViewModel>();
        services.AddSingleton<ApprovalsPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();

        services.AddSingleton<FileContextMenuPage>();
        services.AddSingleton<AllObjectsContextMenuPage>();
        services.AddSingleton<FolderContextMenuPage>();
        services.AddSingleton<DirectoryContextMenuPage>();
        services.AddSingleton<BackgroundContextMenuPage>();
        services.AddSingleton<DesktopContextMenuPage>();
        services.AddSingleton<DriveContextMenuPage>();
        services.AddSingleton<LibraryContextMenuPage>();
        services.AddSingleton<ComputerContextMenuPage>();
        services.AddSingleton<RecycleBinContextMenuPage>();
        services.AddSingleton<FileTypesPage>();
        services.AddSingleton<ShellNewPage>();
        services.AddSingleton<SendToPage>();
        services.AddSingleton<WinXPage>();
        services.AddSingleton<OtherRulesPage>();
        services.AddSingleton<ApprovalsPage>();
        services.AddSingleton<SettingsPage>();

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            services.AddSingleton<Windows11ContextMenuPageViewModel>();
            services.AddSingleton<Windows11ContextMenuPage>();
        }

        return services.BuildServiceProvider();
    }
}
