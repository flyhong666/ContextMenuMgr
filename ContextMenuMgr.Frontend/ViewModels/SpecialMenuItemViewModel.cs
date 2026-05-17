using CommunityToolkit.Mvvm.ComponentModel;
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

    public bool CanDelete => Entry.CanDelete && !IsBusy;

    public bool CanEdit => Entry.CanEdit && Entry.Metadata.GetValueOrDefault("EntryType") != "DefaultDropEffect" && !IsBusy;

    public bool CanMove => Entry.CanMove && !IsBusy;

    public bool CanMoveDown => CanMove && _canMoveDown;

    public bool CanMoveUp => CanMove && _canMoveUp;

    public bool CanToggle => ShowToggle && !IsBusy;

    public bool ShowDelete => Entry.CanDelete;

    public bool ShowEdit => Entry.CanEdit && Entry.Metadata.GetValueOrDefault("EntryType") != "DefaultDropEffect";

    public bool ShowMove => Entry.CanMove;

    public bool ShowToggle => Entry.CanEdit
        && Entry.Metadata.GetValueOrDefault("EntryType") is not ("DefaultDropEffect" or "Separator");

    public bool IsSeparator => Entry.Metadata.GetValueOrDefault("EntryType") == "Separator";

    public ImageSource? IconSource => _iconPreviewService.GetIcon(Entry.IconPath, Entry.IconIndex, Entry.TargetPath ?? Entry.Path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanMove))]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public void Update(SpecialMenuEntry entry)
    {
        Entry = entry;
        _suppressSync = true;
        try
        {
            IsEnabled = entry.IsEnabled;
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
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanMove));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ShowDelete));
        OnPropertyChanged(nameof(ShowEdit));
        OnPropertyChanged(nameof(ShowMove));
        OnPropertyChanged(nameof(ShowToggle));
        OnPropertyChanged(nameof(IsSeparator));
        OnPropertyChanged(nameof(IconSource));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Subtitle));
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
