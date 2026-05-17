using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.Services;
using ContextMenuMgr.Frontend.Views.Pages;
using Microsoft.Win32;
using Wpf.Ui;

namespace ContextMenuMgr.Frontend.ViewModels;

/// <summary>
/// Represents the file Types Page View Model.
/// </summary>
public partial class FileTypesPageViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly IBackendClient _backendClient;
    private readonly INavigationService _navigationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTypesPageViewModel"/> class.
    /// </summary>
    public FileTypesPageViewModel(
        IBackendClient backendClient,
        ContextMenuWorkspaceService workspace,
        LocalizationService localization,
        IconPreviewService iconPreviewService,
        ContextMenuItemActionsService actionsService,
        FrontendSettingsService settingsService,
        INavigationService navigationService)
    {
        _localization = localization;
        _backendClient = backendClient;
        _navigationService = navigationService;

        ShortcutTab = new SceneContextMenuTabViewModel(
            "Link24",
            localization.Translate("SceneLnkFileTitle"),
            localization.Translate("SceneLnkFileDescription"),
            ContextMenuSceneKind.LnkFile,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService,
            fixedScopeValue: "lnkfile");

        UwpShortcutTab = new SceneContextMenuTabViewModel(
            "AppsList24",
            localization.Translate("SceneUwpShortcutTitle"),
            localization.Translate("SceneUwpShortcutDescription"),
            ContextMenuSceneKind.UwpShortcut,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService,
            fixedScopeValue: "Launcher.ImmersiveApplication");

        ExecutableTab = new SceneContextMenuTabViewModel(
            "WindowDevTools24",
            localization.Translate("SceneExeFileTitle"),
            localization.Translate("SceneExeFileDescription"),
            ContextMenuSceneKind.ExeFile,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService,
            fixedScopeValue: "exefile");

        UnknownTypeTab = new SceneContextMenuTabViewModel(
            "DocumentQuestionMark24",
            localization.Translate("SceneUnknownTypeTitle"),
            localization.Translate("SceneUnknownTypeDescription"),
            ContextMenuSceneKind.UnknownType,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService,
            fixedScopeValue: "Unknown");

        CustomExtensionTab = new SceneContextMenuTabViewModel(
            "DocumentText24",
            localization.Translate("SceneCustomExtensionTitle"),
            localization.Translate("SceneCustomExtensionDescription"),
            ContextMenuSceneKind.CustomExtension,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService);
        CustomExtensionTab.ConfigureTextSelector(
            localization.Translate("SceneExtensionSelectorLabel"),
            ".txt");
        _ = CustomExtensionTab.RefreshAsync();

        PerceivedTypeTab = new SceneContextMenuTabViewModel(
            "TextGrammarArrowLeft24",
            localization.Translate("ScenePerceivedTypeTitle"),
            localization.Translate("ScenePerceivedTypeDescription"),
            ContextMenuSceneKind.PerceivedType,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService);
        PerceivedTypeTab.ConfigureOptionSelector(
            localization.Translate("ScenePerceivedTypeSelectorLabel"),
            CreatePerceivedTypeOptions(localization),
            "Text");

        DirectoryTypeTab = new SceneContextMenuTabViewModel(
            "FolderArrowRight24",
            localization.Translate("SceneDirectoryTypeTitle"),
            localization.Translate("SceneDirectoryTypeDescription"),
            ContextMenuSceneKind.DirectoryType,
            backendClient,
            workspace,
            localization,
            iconPreviewService,
            actionsService,
            settingsService);
        DirectoryTypeTab.ConfigureOptionSelector(
            localization.Translate("SceneDirectoryTypeSelectorLabel"),
            CreateDirectoryTypeOptions(localization),
            "Document");

        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Gets the shortcut Tab.
    /// </summary>
    public SceneContextMenuTabViewModel ShortcutTab { get; }

    /// <summary>
    /// Gets the uwp Shortcut Tab.
    /// </summary>
    public SceneContextMenuTabViewModel UwpShortcutTab { get; }

    /// <summary>
    /// Gets the executable Tab.
    /// </summary>
    public SceneContextMenuTabViewModel ExecutableTab { get; }

    /// <summary>
    /// Gets the custom Extension Tab.
    /// </summary>
    public SceneContextMenuTabViewModel CustomExtensionTab { get; }

    /// <summary>
    /// Gets the perceived Type Tab.
    /// </summary>
    public SceneContextMenuTabViewModel PerceivedTypeTab { get; }

    /// <summary>
    /// Gets the directory Type Tab.
    /// </summary>
    public SceneContextMenuTabViewModel DirectoryTypeTab { get; }

    /// <summary>
    /// Gets the unknown Type Tab.
    /// </summary>
    public SceneContextMenuTabViewModel UnknownTypeTab { get; }

    public ObservableCollection<FileTypeAnalysisResult> AnalysisResults { get; } = [];

    public string Title => _localization.Translate("FileTypesPageTitle");

    public string MenuAnalysisTitle => _localization.Translate("MenuAnalysisTitle");

    public string MenuAnalysisDescription => _localization.Translate("MenuAnalysisDescription");

    public string AnalyzeFileText => _localization.Translate("AnalyzeFile");

    public string AnalyzeFolderText => _localization.Translate("AnalyzeFolder");

    public string JumpText => _localization.Translate("JumpToScene");

    [ObservableProperty]
    private int _selectedTabIndex = -1;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ShortcutTab.Title = _localization.Translate("SceneLnkFileTitle");
        ShortcutTab.Description = _localization.Translate("SceneLnkFileDescription");
        UwpShortcutTab.Title = _localization.Translate("SceneUwpShortcutTitle");
        UwpShortcutTab.Description = _localization.Translate("SceneUwpShortcutDescription");
        ExecutableTab.Title = _localization.Translate("SceneExeFileTitle");
        ExecutableTab.Description = _localization.Translate("SceneExeFileDescription");
        CustomExtensionTab.Title = _localization.Translate("SceneCustomExtensionTitle");
        CustomExtensionTab.Description = _localization.Translate("SceneCustomExtensionDescription");
        CustomExtensionTab.SelectorLabel = _localization.Translate("SceneExtensionSelectorLabel");
        PerceivedTypeTab.Title = _localization.Translate("ScenePerceivedTypeTitle");
        PerceivedTypeTab.Description = _localization.Translate("ScenePerceivedTypeDescription");
        PerceivedTypeTab.SelectorLabel = _localization.Translate("ScenePerceivedTypeSelectorLabel");
        PerceivedTypeTab.ConfigureOptionSelector(
            _localization.Translate("ScenePerceivedTypeSelectorLabel"),
            CreatePerceivedTypeOptions(_localization),
            PerceivedTypeTab.SelectedOption?.Value);
        DirectoryTypeTab.Title = _localization.Translate("SceneDirectoryTypeTitle");
        DirectoryTypeTab.Description = _localization.Translate("SceneDirectoryTypeDescription");
        DirectoryTypeTab.ConfigureOptionSelector(
            _localization.Translate("SceneDirectoryTypeSelectorLabel"),
            CreateDirectoryTypeOptions(_localization),
            DirectoryTypeTab.SelectedOption?.Value);
        UnknownTypeTab.Title = _localization.Translate("SceneUnknownTypeTitle");
        UnknownTypeTab.Description = _localization.Translate("SceneUnknownTypeDescription");
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(MenuAnalysisTitle));
        OnPropertyChanged(nameof(MenuAnalysisDescription));
        OnPropertyChanged(nameof(AnalyzeFileText));
        OnPropertyChanged(nameof(AnalyzeFolderText));
        OnPropertyChanged(nameof(JumpText));
    }

    [RelayCommand]
    private async Task AnalyzeFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { DereferenceLinks = false };
        if (dialog.ShowDialog() == true)
        {
            await AnalyzePathAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private async Task AnalyzeFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = AnalyzeFolderText,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog() == true)
        {
            await AnalyzePathAsync(dialog.FolderName);
        }
    }

    [RelayCommand]
    private async Task ApplyAnalysisResultAsync(FileTypeAnalysisResult? result)
    {
        if (result is null)
        {
            return;
        }

        switch (result.SceneKind)
        {
            case ContextMenuSceneKind.LnkFile:
                ShortcutTab.ScopeValue = "lnkfile";
                SelectedTabIndex = 0;
                await ShortcutTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.UwpShortcut:
                UwpShortcutTab.ScopeValue = "Launcher.ImmersiveApplication";
                SelectedTabIndex = 1;
                await UwpShortcutTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.ExeFile:
                ExecutableTab.ScopeValue = "exefile";
                SelectedTabIndex = 2;
                await ExecutableTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.CustomExtension:
                CustomExtensionTab.ScopeValue = result.ScopeValue ?? string.Empty;
                SelectedTabIndex = 3;
                await CustomExtensionTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.PerceivedType:
                PerceivedTypeTab.ScopeValue = result.ScopeValue ?? string.Empty;
                SelectedTabIndex = 4;
                await PerceivedTypeTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.DirectoryType:
                DirectoryTypeTab.ScopeValue = result.ScopeValue ?? "Document";
                SelectedTabIndex = 5;
                await DirectoryTypeTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.UnknownType:
                UnknownTypeTab.ScopeValue = "Unknown";
                SelectedTabIndex = 6;
                await UnknownTypeTab.RefreshAsync();
                break;
            case ContextMenuSceneKind.CustomRegistryPath:
                await HandleCustomRegistryPathJumpAsync(result);
                break;
            default:
                await FrontendMessageBox.ShowErrorAsync(
                    _localization.Format("UnsupportedSceneKind", result.SceneKind),
                    MenuAnalysisTitle);
                break;
        }
    }

    private async Task HandleCustomRegistryPathJumpAsync(FileTypeAnalysisResult result)
    {
        var scopeValue = result.ScopeValue ?? string.Empty;

        if (scopeValue.Contains(@"HKCR\*\shell", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(typeof(FileContextMenuPage));
        }
        else if (scopeValue.Contains(@"HKCR\AllFilesystemObjects\shell", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(typeof(AllObjectsContextMenuPage));
        }
        else if (scopeValue.Contains(@"HKCR\Folder\shell", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(typeof(FolderContextMenuPage));
        }
        else if (scopeValue.Contains(@"HKCR\Drive\shell", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(typeof(DriveContextMenuPage));
        }
        else if (scopeValue.Contains(@"HKCR\Directory\shell", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(typeof(DirectoryContextMenuPage));
        }
        else
        {
            await FrontendMessageBox.ShowInfoAsync(
                _localization.Format("JumpToCustomPathHint", scopeValue),
                MenuAnalysisTitle);
        }
    }

    private async Task AnalyzePathAsync(string path)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var results = await _backendClient.AnalyzeFileTypeContextAsync(path, cts.Token);
            AnalysisResults.Clear();
            foreach (var result in results)
            {
                var localizedResult = LocalizeAnalysisResult(result);
                AnalysisResults.Add(localizedResult);
            }
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, MenuAnalysisTitle);
        }
    }

    private FileTypeAnalysisResult LocalizeAnalysisResult(FileTypeAnalysisResult result)
    {
        string localizedDisplayName;
        string localizedReason;

        if (result.ScopeValue == @"HKCR\*\shell")
        {
            localizedDisplayName = _localization.Translate("AnalysisResultAllFiles");
            localizedReason = _localization.Translate("AnalysisResultAllFilesReason");
        }
        else if (result.ScopeValue == @"HKCR\AllFilesystemObjects\shell")
        {
            localizedDisplayName = _localization.Translate("AnalysisResultAllFilesystemObjects");
            localizedReason = _localization.Translate("AnalysisResultAllFilesystemObjectsReason");
        }
        else if (result.SceneKind == ContextMenuSceneKind.ExeFile)
        {
            localizedDisplayName = _localization.Translate("AnalysisResultExecutable");
            localizedReason = _localization.Translate("AnalysisResultExecutableReason");
        }
        else if (result.SceneKind == ContextMenuSceneKind.CustomExtension)
        {
            var extension = result.ScopeValue ?? string.Empty;
            localizedDisplayName = _localization.Format("AnalysisResultExtension", extension);
            localizedReason = _localization.Translate("AnalysisResultExtensionReason");
        }
        else if (result.SceneKind == ContextMenuSceneKind.LnkFile)
        {
            localizedDisplayName = _localization.Translate("AnalysisResultShortcut");
            localizedReason = _localization.Translate("AnalysisResultShortcutReason");
        }
        else if (result.SceneKind == ContextMenuSceneKind.UnknownType)
        {
            localizedDisplayName = _localization.Translate("AnalysisResultUnknownType");
            localizedReason = _localization.Translate("AnalysisResultUnknownTypeReason");
        }
        else if (result.SceneKind == ContextMenuSceneKind.PerceivedType)
        {
            var perceivedType = result.ScopeValue ?? string.Empty;
            localizedDisplayName = _localization.Format("AnalysisResultPerceivedType", perceivedType);
            localizedReason = _localization.Translate("AnalysisResultPerceivedTypeReason");
        }
        else if (result.ScopeValue == @"HKCR\Folder\shell")
        {
            localizedDisplayName = _localization.Translate("AnalysisResultFolder");
            localizedReason = _localization.Translate("AnalysisResultFolderReason");
        }
        else if (result.ScopeValue == @"HKCR\Drive\shell")
        {
            localizedDisplayName = _localization.Translate("AnalysisResultDrive");
            localizedReason = _localization.Translate("AnalysisResultDriveReason");
        }
        else if (result.ScopeValue == @"HKCR\Directory\shell")
        {
            localizedDisplayName = _localization.Translate("AnalysisResultDirectory");
            localizedReason = _localization.Translate("AnalysisResultDirectoryReason");
        }
        else if (result.SceneKind == ContextMenuSceneKind.DirectoryType)
        {
            localizedDisplayName = _localization.Translate("AnalysisResultDirectoryType");
            localizedReason = _localization.Translate("AnalysisResultDirectoryTypeReason");
        }
        else
        {
            localizedDisplayName = result.DisplayName;
            localizedReason = result.Reason;
        }

        return result with { DisplayName = localizedDisplayName, Reason = localizedReason };
    }

    private static IReadOnlyList<SceneOptionViewModel> CreatePerceivedTypeOptions(LocalizationService localization)
    {
        return
        [
            new("Text", localization.Translate("PerceivedTypeText")),
            new("Document", localization.Translate("PerceivedTypeDocument")),
            new("Image", localization.Translate("PerceivedTypeImage")),
            new("Video", localization.Translate("PerceivedTypeVideo")),
            new("Audio", localization.Translate("PerceivedTypeAudio")),
            new("Compressed", localization.Translate("PerceivedTypeCompressed")),
            new("System", localization.Translate("PerceivedTypeSystem"))
        ];
    }

    private static IReadOnlyList<SceneOptionViewModel> CreateDirectoryTypeOptions(LocalizationService localization)
    {
        return
        [
            new("Document", localization.Translate("DirectoryTypeDocument")),
            new("Image", localization.Translate("DirectoryTypeImage")),
            new("Video", localization.Translate("DirectoryTypeVideo")),
            new("Audio", localization.Translate("DirectoryTypeAudio"))
        ];
    }

    /// <summary>
    /// Executes dispose.
    /// </summary>
    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        ShortcutTab.Dispose();
        UwpShortcutTab.Dispose();
        ExecutableTab.Dispose();
        CustomExtensionTab.Dispose();
        PerceivedTypeTab.Dispose();
        DirectoryTypeTab.Dispose();
        UnknownTypeTab.Dispose();
    }
}
