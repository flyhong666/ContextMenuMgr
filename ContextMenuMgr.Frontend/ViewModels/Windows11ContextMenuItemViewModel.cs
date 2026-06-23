using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.Services;

namespace ContextMenuMgr.Frontend.ViewModels;

/// <summary>
/// Represents the windows11 Context Menu Item View Model.
/// </summary>
public partial class Windows11ContextMenuItemViewModel : ObservableObject, IDisposable
{
    private readonly Windows11ContextMenuService _service;
    private readonly LocalizationService _localization;
    private readonly EventHandler _languageChangedHandler;
    private bool _suppressSync;
    private readonly Windows11ContextMenuItemDefinition _primaryDefinition;

    /// <summary>
    /// Initializes a new instance of the <see cref="Windows11ContextMenuItemViewModel"/> class.
    /// </summary>
    public Windows11ContextMenuItemViewModel(
        IReadOnlyList<Windows11ContextMenuItemDefinition> definitions,
        Windows11ContextMenuService service,
        LocalizationService localization)
    {
        Definitions = definitions;
        _primaryDefinition = definitions[0];
        _service = service;
        _localization = localization;

        _logoTask = Windows11ContextMenuService.LoadLogo(_primaryDefinition.Package, CancellationToken.None);
        RefreshState(definitions.All(static definition => definition.IsEnabled));

        _languageChangedHandler = (_, _) =>
        {
            OnPropertyChanged(nameof(ToggleOnText));
            OnPropertyChanged(nameof(ToggleOffText));
            OnPropertyChanged(nameof(ContextTypesText));
            OnPropertyChanged(nameof(MachineBlockedText));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(ProtectedText));
            OnPropertyChanged(nameof(GuidLockWarningText));
        };
        _localization.LanguageChanged += _languageChangedHandler;
    }

    private readonly Task<ImageSource?> _logoTask;

    /// <summary>
    /// Gets the grouped source definitions.
    /// </summary>
    public IReadOnlyList<Windows11ContextMenuItemDefinition> Definitions { get; }

    public string DisplayName => _primaryDefinition.DisplayName;

    public string PublisherName => _primaryDefinition.Package.PublisherDisplayName ?? string.Empty;

    public string PackageFamilyName => _primaryDefinition.Package.FamilyName;

    public string InstallPath => _primaryDefinition.Package.InstallPath;

    public string ComServerPath => _primaryDefinition.ComServer.Path ?? string.Empty;

    public string CommandKey => _primaryDefinition.Entry?.KeyName ?? string.Empty;

    public string RegistryPath => _primaryDefinition.Entry?.RegistryPath ?? string.Empty;

    public string HandlerGuid => _primaryDefinition.Entry?.HandlerClsid ?? string.Empty;

    public string ContextTypesText => string.Join(
        "  ·  ",
        Definitions
            .SelectMany(static definition => definition.ContextTypes)
            .Where(static type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(LocalizeContextType));

    public bool HasComServerPath => !string.IsNullOrWhiteSpace(_primaryDefinition.ComServer.Path);

    public bool HasCommandKey => IsSystemCommand && !string.IsNullOrWhiteSpace(CommandKey);

    public bool HasRegistryPath => !string.IsNullOrWhiteSpace(RegistryPath);

    public bool HasHandlerGuid => !string.IsNullOrWhiteSpace(HandlerGuid);

    public bool HasPublisherName => !string.IsNullOrWhiteSpace(_primaryDefinition.Package.PublisherDisplayName);

    public ImageSource? LogoSource => _logoTask.IsCompletedSuccessfully ? _logoTask.Result : null;

    public bool HasLogo => LogoSource is not null;

    public string ToggleOnText => _localization.Translate("ToggleOn");

    public string ToggleOffText => _localization.Translate("ToggleOff");

    public string MachineBlockedText => _localization.Translate("Windows11MachineBlockedText");

    public string SourceLabel => IsSystemCommand
        ? _localization.Translate("Windows11SystemCommandSourceLabel")
        : _localization.Translate("Windows11PackagedComSourceLabel");

    public string ProtectedText => _localization.Translate("Windows11SystemCommandProtectedText");

    public string GuidLockWarningText => _localization.Translate("Windows11SystemCommandGuidLockWarning");

    public string OpenFileLocationText => _localization.Translate("DetailsFileLocation");

    public string PendingApprovalText => _localization.Translate("PendingApprovalBadge");

    /// <summary>
    /// Gets or sets a value indicating whether enabled.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether busy.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(CanOpenFileLocation))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileLocationCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this grouped item is pending approval.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPendingApproval { get; set; }

    public bool IsMachineBlocked => Definitions.Any(static definition => definition.IsMachineBlocked);

    public bool IsSystemCommand => _primaryDefinition.SourceKind == Windows11ContextMenuSourceKind.SystemCommandStore;

    public bool IsProtected => Definitions.Any(static definition => definition.IsProtected);

    public bool ShowGuidLockWarning => IsSystemCommand;

    public bool CanToggle => !IsBusy && !IsMachineBlocked && !IsProtected;

    public bool CanOpenFileLocation => !IsBusy
        && !string.IsNullOrWhiteSpace(InstallPath)
        && Directory.Exists(InstallPath);

    /// <summary>
    /// Refreshes state.
    /// </summary>
    public void RefreshState(bool isEnabled)
    {
        _suppressSync = true;
        try
        {
            IsEnabled = isEnabled;
        }
        finally
        {
            _suppressSync = false;
        }
    }

    /// <summary>
    /// Refreshes the pending-approval state for the grouped item.
    /// </summary>
    public void RefreshPendingApproval(bool isPendingApproval)
    {
        IsPendingApproval = isPendingApproval;
    }

    partial void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        if (_suppressSync || oldValue == newValue)
        {
            return;
        }

        IsBusy = true;
        _ = SyncAsync(oldValue, newValue);
    }

    private async Task SyncAsync(bool oldValue, bool newValue)
    {
        try
        {
            foreach (var definition in Definitions.DistinctBy(static definition => definition.ComServer.Id ?? definition.Id))
            {
                await _service.SetEnabledAsync(definition.Id, definition.DisplayName, newValue, CancellationToken.None);
            }

            RefreshState(newValue);
        }
        catch (Exception ex)
        {
            _suppressSync = true;
            try
            {
                IsEnabled = oldValue;
            }
            finally
            {
                _suppressSync = false;
            }

            await FrontendMessageBox.ShowErrorAsync(ex.Message, DisplayName);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenFileLocation))]
    private async Task OpenFileLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallPath) || !Directory.Exists(InstallPath))
        {
            await FrontendMessageBox.ShowErrorAsync(
                _localization.Translate("ModulePathUnavailable"),
                DisplayName);
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{InstallPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, DisplayName);
        }
    }

    private string LocalizeContextType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return string.Empty;
        }

        return type switch
        {
            "Directory" => _localization.Translate("FolderCategoryName"),
            "Directory\\Background" => _localization.Translate("BackgroundCategoryName"),
            "Drive" => _localization.Translate("DriveCategoryName"),
            "*" => _localization.Translate("FileCategoryName"),
            "DesktopBackground" => _localization.Translate("DesktopCategoryName"),
            _ => type
        };
    }

    /// <summary>
    /// Executes dispose.
    /// </summary>
    public void Dispose()
    {
        _localization.LanguageChanged -= _languageChangedHandler;
    }
}
