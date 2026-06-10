using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.Services;
using System.Windows.Media;

namespace ContextMenuMgr.Frontend.ViewModels;

/// <summary>
/// Represents a special menu item view model.
/// </summary>
public partial class SpecialMenuItemViewModel : ObservableObject
{
    private readonly IconPreviewService _iconPreviewService;
    private readonly LocalizationService _localization;
    private readonly Func<SpecialMenuItemViewModel, bool, Task<bool>> _setEnabledAsync;
    private bool _canMoveDown = true;
    private bool _canMoveUp = true;
    private bool _suppressSync;

    public SpecialMenuItemViewModel(
        SpecialMenuEntry entry,
        IconPreviewService iconPreviewService,
        LocalizationService localization,
        Func<SpecialMenuItemViewModel, bool, Task<bool>> setEnabledAsync)
    {
        Entry = entry;
        _iconPreviewService = iconPreviewService;
        _localization = localization;
        _setEnabledAsync = setEnabledAsync;
        _suppressSync = true;
        try
        {
            IsEnabled = entry.IsEnabled;
            IsDeleted = entry.Metadata.TryGetValue("IsDeleted", out var deleted) && bool.TryParse(deleted, out var isDeleted) && isDeleted;
        }
        finally
        {
            _suppressSync = false;
        }
    }

    public SpecialMenuEntry Entry { get; private set; }

    public string Id => Entry.Id;

    public string DisplayName => Entry.Metadata.TryGetValue("LocalizationKey", out var key) && !string.IsNullOrWhiteSpace(key)
        ? _localization.Translate(key)
        : Entry.DisplayName;

    public string KeyName => Entry.KeyName;

    public string Subtitle => Entry.GroupName is { Length: > 0 } groupName
        ? _localization.Translate(groupName)
        : Entry.Path ?? Entry.RegistryPath ?? string.Empty;

    public string Detail => Entry.CommandText ?? Entry.TargetPath ?? Entry.Notes ?? string.Empty;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool CanDelete => Entry.CanDelete && !IsBusy && !IsDeleted;

    public bool CanUndoDelete => Entry.Kind != SpecialMenuKind.WinX && Entry.CanDelete && !IsBusy && IsDeleted;

    public bool CanPermanentlyDelete => Entry.Kind != SpecialMenuKind.WinX && Entry.CanDelete && !IsBusy && IsDeleted;

    public bool CanEdit => Entry.CanEdit && Entry.Metadata.GetValueOrDefault("EntryType") != "DefaultDropEffect" && !IsBusy && !IsDeleted;

    public bool CanMove => Entry.CanMove && !IsBusy && !IsDeleted;

    public bool CanMoveDown => CanMove && _canMoveDown;

    public bool CanMoveUp => CanMove && _canMoveUp;

    public bool CanToggle => ShowToggle && !IsBusy && !IsDeleted;

    public bool ShowDelete => Entry.CanDelete && !IsDeleted;

    public bool ShowUndoDelete => Entry.Kind != SpecialMenuKind.WinX && Entry.CanDelete && IsDeleted;

    public bool ShowPermanentDelete => Entry.Kind != SpecialMenuKind.WinX && Entry.CanDelete && IsDeleted;

    public bool ShowEdit => Entry.CanEdit && Entry.Metadata.GetValueOrDefault("EntryType") != "DefaultDropEffect" && !IsDeleted;

    public bool ShowMove => Entry.CanMove && !IsDeleted;

    public bool ShowToggle => Entry.CanEdit
        && Entry.Metadata.GetValueOrDefault("EntryType") is not ("DefaultDropEffect" or "Separator")
        && !IsDeleted;

    public bool IsSeparator => Entry.Metadata.GetValueOrDefault("EntryType") == "Separator";

    public ImageSource? IconSource => _iconPreviewService.GetIcon(Entry.IconPath, Entry.IconIndex, Entry.TargetPath ?? Entry.Path);

    public string DeletedAtText => _localization.Format("DeletedAt", Entry.Metadata.TryGetValue("DeletedAt", out var deletedAt) ? deletedAt : string.Empty);

    public string PermanentDeleteConfirmationText => _localization.Format("PermanentDeletePrompt", DisplayName);

    public double CardOpacity => IsDeleted ? 0.6 : 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanUndoDelete))]
    [NotifyPropertyChangedFor(nameof(CanPermanentlyDelete))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanMove))]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(ShowDelete))]
    [NotifyPropertyChangedFor(nameof(ShowUndoDelete))]
    [NotifyPropertyChangedFor(nameof(ShowPermanentDelete))]
    [NotifyPropertyChangedFor(nameof(ShowEdit))]
    [NotifyPropertyChangedFor(nameof(ShowMove))]
    [NotifyPropertyChangedFor(nameof(ShowToggle))]
    [NotifyPropertyChangedFor(nameof(CardOpacity))]
    public partial bool IsDeleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanUndoDelete))]
    [NotifyPropertyChangedFor(nameof(CanPermanentlyDelete))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanMove))]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsPermanentDeleteFlyoutOpen { get; set; }

    [RelayCommand]
    private void ClosePermanentDeleteFlyout()
    {
        IsPermanentDeleteFlyoutOpen = false;
    }

    public void Update(SpecialMenuEntry entry)
    {
        Entry = entry;
        _suppressSync = true;
        try
        {
            IsEnabled = entry.IsEnabled;
            var newIsDeleted = entry.Metadata.TryGetValue("IsDeleted", out var deleted) && bool.TryParse(deleted, out var isDeleted) && isDeleted;
            if (IsDeleted != newIsDeleted)
            {
                IsDeleted = newIsDeleted;
            }
        }
        finally
        {
            _suppressSync = false;
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(KeyName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanUndoDelete));
        OnPropertyChanged(nameof(CanPermanentlyDelete));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanMove));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ShowDelete));
        OnPropertyChanged(nameof(ShowUndoDelete));
        OnPropertyChanged(nameof(ShowPermanentDelete));
        OnPropertyChanged(nameof(ShowEdit));
        OnPropertyChanged(nameof(ShowMove));
        OnPropertyChanged(nameof(ShowToggle));
        OnPropertyChanged(nameof(IsSeparator));
        OnPropertyChanged(nameof(IconSource));
        OnPropertyChanged(nameof(DeletedAtText));
        OnPropertyChanged(nameof(CardOpacity));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DeletedAtText));
    }

    public void SetMoveAvailability(bool canMoveUp, bool canMoveDown)
    {
        if (_canMoveUp == canMoveUp && _canMoveDown == canMoveDown)
        {
            return;
        }

        _canMoveUp = canMoveUp;
        _canMoveDown = canMoveDown;
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
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
            if (!await _setEnabledAsync(this, newValue))
            {
                Revert(oldValue);
            }
        }
        catch
        {
            Revert(oldValue);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Revert(bool value)
    {
        _suppressSync = true;
        try
        {
            IsEnabled = value;
        }
        finally
        {
            _suppressSync = false;
        }
    }
}
