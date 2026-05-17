using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.Services;

namespace ContextMenuMgr.Frontend.ViewModels;

/// <summary>
/// Represents a special menu page view model.
/// </summary>
public partial class SpecialMenuPageViewModel : ObservableObject, IDisposable
{
    private readonly IBackendClient _backendClient;
    private readonly IconPreviewService _iconPreviewService;
    private readonly LocalizationService _localization;
    private readonly ExplorerRestartStateService _explorerRestartState;
    private readonly string _titleKey;
    private readonly string _descriptionKey;
    private readonly Dictionary<string, bool> _winXExpandedStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressShellNewLockSync;
    private bool _suppressDropEffectSync;

    public SpecialMenuPageViewModel(
        SpecialMenuKind kind,
        string titleKey,
        string descriptionKey,
        IBackendClient backendClient,
        IconPreviewService iconPreviewService,
        LocalizationService localization,
        ExplorerRestartStateService explorerRestartState)
    {
        Kind = kind;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _backendClient = backendClient;
        _iconPreviewService = iconPreviewService;
        _localization = localization;
        _explorerRestartState = explorerRestartState;
        ItemsView = new ListCollectionView(Items);
        ItemsView.Filter = FilterItem;
        SearchLabel = _localization.Translate("SearchLabel");
        AddText = _localization.Translate("Add");
        EditText = _localization.Translate("Edit");
        RefreshText = _localization.Translate("Refresh");
        RestoreDefaultsText = _localization.Translate("RestoreDefault");
        LockNewMenuText = _localization.Translate("LockNewMenu");
        DeleteText = _localization.Translate("Delete");
        UndoText = _localization.Translate("Undo");
        PermanentDeleteText = _localization.Translate("PermanentDelete");
        CancelText = _localization.Translate("DialogCancel");
        DropEffectLabel = _localization.Translate("DefaultDropEffect");
        DropEffectOptions =
        [
            DefaultDropEffect.Default,
            DefaultDropEffect.Copy,
            DefaultDropEffect.Move,
            DefaultDropEffect.CreateLink
        ];
        _backendClient.NotificationReceived += OnNotificationReceived;
        _localization.LanguageChanged += OnLanguageChanged;
        _ = RefreshAsync();
    }

    public SpecialMenuKind Kind { get; }

    public ObservableCollection<SpecialMenuItemViewModel> Items { get; } = [];

    public ObservableCollection<WinXGroupNodeViewModel> WinXGroups { get; } = [];

    public ICollectionView ItemsView { get; }

    public string Title => _localization.Translate(_titleKey);

    public string Description => _localization.Translate(_descriptionKey);

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchLabel { get; set; }

    [ObservableProperty]
    public partial string AddText { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; }

    [ObservableProperty]
    public partial string RefreshText { get; set; }

    [ObservableProperty]
    public partial string RestoreDefaultsText { get; set; }

    [ObservableProperty]
    public partial string LockNewMenuText { get; set; }

    [ObservableProperty]
    public partial string DeleteText { get; set; }

    [ObservableProperty]
    public partial string UndoText { get; set; }

    [ObservableProperty]
    public partial string PermanentDeleteText { get; set; }

    [ObservableProperty]
    public partial string CancelText { get; set; }

    [ObservableProperty]
    public partial string DropEffectLabel { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DefaultDropEffect> DropEffectOptions { get; set; } = [];

    [ObservableProperty]
    public partial DefaultDropEffect SelectedDropEffect { get; set; }

    [ObservableProperty]
    public partial bool IsShellNewOrderLocked { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool ShowShellNewOrderLock => Kind == SpecialMenuKind.ShellNew;

    public bool ShowRestoreDefaults => Kind is SpecialMenuKind.SendTo or SpecialMenuKind.WinX;

    public bool ShowDropEffectSelector => Kind == SpecialMenuKind.DragDrop;

    public bool ShowWinXTree => Kind == SpecialMenuKind.WinX;

    partial void OnSearchTextChanged(string value) => ItemsView.Refresh();

    partial void OnIsShellNewOrderLockedChanged(bool oldValue, bool newValue)
    {
        if (_suppressShellNewLockSync || oldValue == newValue || Kind != SpecialMenuKind.ShellNew)
        {
            return;
        }

        _ = SetShellNewOrderLockAsync(oldValue, newValue);
    }

    partial void OnSelectedDropEffectChanged(DefaultDropEffect oldValue, DefaultDropEffect newValue)
    {
        if (_suppressDropEffectSync || oldValue == newValue || Kind != SpecialMenuKind.DragDrop)
        {
            return;
        }

        _ = SetDefaultDropEffectAsync(oldValue, newValue);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ApplySnapshot(await _backendClient.GetSpecialMenuSnapshotAsync(Kind, cts.Token));
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        try
        {
            var operationId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = await CreateAddRequestAsync(operationId);
            if (request is null)
            {
                return;
            }

            var item = await _backendClient.CreateSpecialMenuItemAsync(request, cts.Token);
            if (item is not null)
            {
                if (Kind == SpecialMenuKind.WinX)
                {
                    _explorerRestartState.MarkRequired();
                    await RefreshAsync();
                }
                else
                {
                    Upsert(item);
                }
            }
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, Title);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(SpecialMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            item.IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var updated = await _backendClient.DeleteSpecialMenuItemAsync(item.Entry, Guid.NewGuid(), cts.Token);
            if (updated is not null)
            {
                item.Update(updated);
            }
            else
            {
                Items.Remove(item);
            }

            if (Kind == SpecialMenuKind.WinX)
            {
                _explorerRestartState.MarkRequired();
            }
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, item.DisplayName);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UndoDeleteAsync(SpecialMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            item.IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var updated = await _backendClient.UndoDeleteSpecialMenuItemAsync(item.Entry, Guid.NewGuid(), cts.Token);
            if (updated is not null)
            {
                item.Update(updated);
            }
            else
            {
                Items.Remove(item);
            }

            if (Kind == SpecialMenuKind.WinX)
            {
                _explorerRestartState.MarkRequired();
            }
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, item.DisplayName);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenPermanentDeleteFlyoutAsync(SpecialMenuItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsPermanentDeleteFlyoutOpen = true;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ConfirmPermanentDeleteAsync(SpecialMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsPermanentDeleteFlyoutOpen = false;
        try
        {
            item.IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _backendClient.PurgeDeletedSpecialMenuItemAsync(item.Entry, Guid.NewGuid(), cts.Token);
            Items.Remove(item);
            if (Kind == SpecialMenuKind.WinX)
            {
                _explorerRestartState.MarkRequired();
            }
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, item.DisplayName);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EditAsync(SpecialMenuItemViewModel? item)
    {
        if (item is null || !item.CanEdit)
        {
            return;
        }

        try
        {
            var operationId = Guid.NewGuid();
            var request = await CreateUpdateRequestAsync(item, operationId);
            if (request is null)
            {
                return;
            }

            item.IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var updated = await _backendClient.UpdateSpecialMenuItemAsync(request, cts.Token);
            if (updated is not null)
            {
                if (Kind == SpecialMenuKind.WinX)
                {
                    _explorerRestartState.MarkRequired();
                    await RefreshAsync();
                }
                else
                {
                    item.Update(updated);
                }
            }
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, item.DisplayName);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MoveUpAsync(SpecialMenuItemViewModel? item) => await MoveAsync(item, true);

    [RelayCommand]
    private async Task MoveDownAsync(SpecialMenuItemViewModel? item) => await MoveAsync(item, false);

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        if (Kind is not (SpecialMenuKind.SendTo or SpecialMenuKind.WinX))
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _backendClient.RestoreSpecialMenuDefaultsAsync(Kind, null, Guid.NewGuid(), cts.Token);
            if (Kind == SpecialMenuKind.WinX)
            {
                _explorerRestartState.MarkRequired();
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, Title);
        }
    }

    private async Task MoveAsync(SpecialMenuItemViewModel? item, bool moveUp)
    {
        if (item is null)
        {
            return;
        }

        if ((moveUp && !item.CanMoveUp) || (!moveUp && !item.CanMoveDown))
        {
            return;
        }

        try
        {
            StatusText = string.Empty;
            item.IsBusy = true;
            var operationId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var request = Kind == SpecialMenuKind.ShellNew
                ? new PipeRequest
                {
                    SpecialKind = Kind,
                    ShellNewSort = new ShellNewSortRequest(item.Id, moveUp),
                    ClientOperationId = operationId
                }
                : new PipeRequest
                {
                    SpecialKind = Kind,
                    WinXMove = new WinXMoveRequest(item.Id, moveUp),
                    ClientOperationId = operationId
                };
            var updated = await _backendClient.MoveSpecialMenuItemAsync(request, cts.Token);
            if (updated is not null)
            {
                if (Kind == SpecialMenuKind.WinX)
                {
                    _explorerRestartState.MarkRequired();
                }
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            if (Kind == SpecialMenuKind.ShellNew && IsExpectedShellNewMoveFailure(ex))
            {
                StatusText = ex.Message;
                return;
            }

            await FrontendMessageBox.ShowErrorAsync(ex.Message, item.DisplayName);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task<bool> SetEnabledAsync(SpecialMenuItemViewModel item, bool enabled)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var updated = await _backendClient.SetSpecialMenuItemEnabledAsync(item.Entry, enabled, Guid.NewGuid(), cts.Token);
            if (updated is not null)
            {
                item.Update(updated);
                if (Kind == SpecialMenuKind.WinX)
                {
                    _explorerRestartState.MarkRequired();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            await FrontendMessageBox.ShowErrorAsync(ex.Message, item.DisplayName);
            return false;
        }
    }

    private async Task<PipeRequest?> CreateAddRequestAsync(Guid operationId)
    {
        switch (Kind)
        {
            case SpecialMenuKind.ShellNew:
                var shellNewAddData = await MenuItemFormDialog.ShowAddShellNewAsync(Title, _localization);
                return shellNewAddData is null
                    ? null
                    : new PipeRequest { SpecialKind = Kind, ShellNewCreate = new ShellNewCreateRequest(shellNewAddData.Extension), ClientOperationId = operationId };
            case SpecialMenuKind.SendTo:
                var sendToData = await MenuItemFormDialog.ShowAddSendToAsync(Title, _localization);
                if (sendToData is null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(sendToData.Name))
                {
                    await FrontendMessageBox.ShowErrorAsync(_localization.Translate("TextCannotBeEmpty"), Title);
                    return null;
                }

                return new PipeRequest { SpecialKind = Kind, SendToCreate = new SendToCreateRequest(sendToData.Name, sendToData.TargetPath, sendToData.Arguments), ClientOperationId = operationId };
            case SpecialMenuKind.WinX:
                var winxData = await MenuItemFormDialog.ShowAddWinXAsync(Title, _localization);
                if (winxData is null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(winxData.Name))
                {
                    await FrontendMessageBox.ShowErrorAsync(_localization.Translate("TextCannotBeEmpty"), Title);
                    return null;
                }

                if (string.Equals(winxData.GroupName, "Group:", StringComparison.OrdinalIgnoreCase) || winxData.GroupName.StartsWith("Group:", StringComparison.OrdinalIgnoreCase))
                {
                    return new PipeRequest { SpecialKind = Kind, WinXCreateGroup = new WinXCreateGroupRequest(winxData.GroupName.Length > 6 ? winxData.GroupName[6..].Trim() : string.Empty), ClientOperationId = operationId };
                }

                return new PipeRequest { SpecialKind = Kind, WinXCreateEntry = new WinXCreateEntryRequest(winxData.Name, winxData.TargetPath, winxData.GroupName, winxData.Arguments), ClientOperationId = operationId };
            case SpecialMenuKind.DragDrop:
                var dragDrop = await TextInputDialog.ShowAsync(Title, "GUID|Group(Folder/Directory/Drive/AllFilesystemObjects)", "{00000000-0000-0000-0000-000000000000}|Folder");
                var dragDropParts = SplitParts(dragDrop, 2);
                return dragDropParts is null
                    ? null
                    : new PipeRequest { SpecialKind = Kind, DragDropCreate = new DragDropCreateRequest(dragDropParts[0], dragDropParts[1]), ClientOperationId = operationId };
            case SpecialMenuKind.CommandStore:
                var commandStore = await TextInputDialog.ShowAsync(Title, "Key|Name|Command", "my.command|My Command|notepad.exe \"%1\"");
                var commandStoreParts = SplitParts(commandStore, 3);
                return commandStoreParts is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        SpecialItem = new SpecialMenuEntry { Kind = Kind, KeyName = commandStoreParts[0], DisplayName = commandStoreParts[1], CommandText = commandStoreParts[2] },
                        ClientOperationId = operationId
                    };
            case SpecialMenuKind.GuidBlock:
                var guid = await TextInputDialog.ShowAsync(Title, "GUID", "{00000000-0000-0000-0000-000000000000}");
                return string.IsNullOrWhiteSpace(guid)
                    ? null
                    : new PipeRequest { SpecialKind = Kind, GuidBlockCreate = new GuidBlockCreateRequest(guid), ClientOperationId = operationId };
            case SpecialMenuKind.InternetExplorer:
                var ie = await TextInputDialog.ShowAsync(Title, "Name|Command", "Open with Notepad|notepad.exe");
                var ieParts = SplitParts(ie, 2);
                return ieParts is null
                    ? null
                    : new PipeRequest { SpecialKind = Kind, IeMenuCreate = new IeMenuCreateRequest(ieParts[0], ieParts[1]), ClientOperationId = operationId };
        }

        return null;
    }

    private async Task<PipeRequest?> CreateUpdateRequestAsync(SpecialMenuItemViewModel item, Guid operationId)
    {
        switch (Kind)
        {
            case SpecialMenuKind.ShellNew:
                var shellNewInitialData = new ShellNewFormData
                {
                    Extension = item.Id,
                    DisplayName = item.DisplayName,
                    IconPath = item.Entry.IconPath ?? string.Empty,
                    Command = item.Entry.CommandText ?? string.Empty,
                    BeforeSeparator = false
                };
                var shellNewEditData = await MenuItemFormDialog.ShowEditShellNewAsync(Title, shellNewInitialData, _localization);
                return shellNewEditData is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        ShellNewUpdate = new ShellNewUpdateRequest(
                            shellNewEditData.Extension,
                            string.IsNullOrWhiteSpace(shellNewEditData.DisplayName) ? null : shellNewEditData.DisplayName,
                            string.IsNullOrWhiteSpace(shellNewEditData.IconPath) ? null : shellNewEditData.IconPath,
                            string.IsNullOrWhiteSpace(shellNewEditData.Command) ? null : shellNewEditData.Command,
                            null,
                            shellNewEditData.BeforeSeparator),
                        ClientOperationId = operationId
                    };
            case SpecialMenuKind.SendTo:
                var sendToInitialData = new MenuItemFormData
                {
                    Name = item.DisplayName,
                    TargetPath = item.Entry.TargetPath ?? string.Empty,
                    Arguments = item.Entry.Arguments ?? string.Empty,
                    WorkingDirectory = item.Entry.WorkingDirectory ?? string.Empty,
                    IconPath = item.Entry.IconPath ?? string.Empty
                };
                var sendToData = await MenuItemFormDialog.ShowEditSendToAsync(Title, sendToInitialData, _localization);
                if (sendToData is null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(sendToData.Name))
                {
                    await FrontendMessageBox.ShowErrorAsync(_localization.Translate("TextCannotBeEmpty"), Title);
                    return null;
                }

                return new PipeRequest
                {
                    SpecialKind = Kind,
                    SendToUpdate = new SendToUpdateRequest(
                        item.Id,
                        sendToData.Name,
                        sendToData.TargetPath,
                        sendToData.Arguments,
                        sendToData.WorkingDirectory,
                        sendToData.IconPath,
                        sendToData.RunAsAdmin),
                    ClientOperationId = operationId
                };
            case SpecialMenuKind.WinX:
                var winxInitialData = new MenuItemFormData
                {
                    Name = item.DisplayName,
                    TargetPath = item.Entry.TargetPath ?? string.Empty,
                    Arguments = item.Entry.Arguments ?? string.Empty,
                    WorkingDirectory = item.Entry.WorkingDirectory ?? string.Empty,
                    IconPath = item.Entry.IconPath ?? string.Empty,
                    GroupName = item.Entry.GroupName ?? string.Empty
                };
                var winxData = await MenuItemFormDialog.ShowEditWinXAsync(Title, winxInitialData, _localization);
                if (winxData is null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(winxData.Name))
                {
                    await FrontendMessageBox.ShowErrorAsync(_localization.Translate("TextCannotBeEmpty"), Title);
                    return null;
                }

                return new PipeRequest
                {
                    SpecialKind = Kind,
                    WinXUpdateEntry = new WinXUpdateEntryRequest(
                        item.Id,
                        winxData.Name,
                        winxData.TargetPath,
                        winxData.Arguments,
                        winxData.WorkingDirectory,
                        winxData.GroupName,
                        winxData.RunAsAdmin),
                    ClientOperationId = operationId
                };
            case SpecialMenuKind.CommandStore:
                var commandStore = await TextInputDialog.ShowAsync(Title, "Name|Command|Icon", $"{item.DisplayName}|{item.Entry.CommandText}|{item.Entry.IconPath}");
                var commandStoreParts = SplitParts(commandStore, 3, allowEmptyMiddleParts: true);
                return commandStoreParts is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        SpecialItem = item.Entry with
                        {
                            DisplayName = commandStoreParts[0],
                            CommandText = EmptyToNull(commandStoreParts[1]),
                            IconPath = EmptyToNull(commandStoreParts[2])
                        },
                        ClientOperationId = operationId
                    };
            case SpecialMenuKind.InternetExplorer:
                var ie = await TextInputDialog.ShowAsync(Title, "Name|Command", $"{item.DisplayName}|{item.Entry.CommandText}");
                var ieParts = SplitParts(ie, 2, allowEmptyMiddleParts: true);
                return ieParts is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        IeMenuUpdate = new IeMenuUpdateRequest(item.Id, EmptyToNull(ieParts[0]), EmptyToNull(ieParts[1])),
                        ClientOperationId = operationId
                    };
        }

        return null;
    }

    private void ApplySnapshot(IEnumerable<SpecialMenuEntry> snapshot)
    {
        var entries = snapshot.ToArray();
        var existing = new Dictionary<string, SpecialMenuItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            existing[item.Id] = item;
        }

        Items.Clear();
        foreach (var entry in entries)
        {
            if (existing.TryGetValue(entry.Id, out var current))
            {
                current.Update(entry);
            }
            else
            {
                current = new SpecialMenuItemViewModel(entry, _iconPreviewService, _localization, SetEnabledAsync);
            }

            Items.Add(current);
        }

        UpdatePageStateFromSnapshot(entries);
        UpdateShellNewMoveAvailability();
        RebuildWinXGroups();
        ItemsView.Refresh();
    }

    private void Upsert(SpecialMenuEntry entry)
    {
        if (Kind == SpecialMenuKind.WinX)
        {
            _ = RefreshAsync();
            return;
        }

        var current = Items.FirstOrDefault(item => string.Equals(item.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            Items.Add(new SpecialMenuItemViewModel(entry, _iconPreviewService, _localization, SetEnabledAsync));
        }
        else
        {
            current.Update(entry);
        }

        UpdateShellNewMoveAvailability();
        ItemsView.Refresh();
        RebuildWinXGroups();
    }

    private bool FilterItem(object obj)
    {
        if (obj is not SpecialMenuItemViewModel item)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return Contains(item.DisplayName, search)
               || Contains(item.KeyName, search)
               || Contains(item.Subtitle, search)
               || Contains(item.Detail, search);
    }

    private void OnNotificationReceived(object? sender, BackendNotification notification)
    {
        if (notification.SpecialKind != Kind || notification.SpecialItem is null)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (Kind == SpecialMenuKind.WinX)
            {
                _ = RefreshAsync();
                return;
            }

            Upsert(notification.SpecialItem);
        });
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        SearchLabel = _localization.Translate("SearchLabel");
        AddText = _localization.Translate("Add");
        EditText = _localization.Translate("Edit");
        RefreshText = _localization.Translate("Refresh");
        RestoreDefaultsText = _localization.Translate("RestoreDefault");
        LockNewMenuText = _localization.Translate("LockNewMenu");
        DeleteText = _localization.Translate("Delete");
        UndoText = _localization.Translate("Undo");
        PermanentDeleteText = _localization.Translate("PermanentDelete");
        CancelText = _localization.Translate("DialogCancel");
        DropEffectLabel = _localization.Translate("DefaultDropEffect");
        foreach (var item in Items)
        {
            item.RefreshLocalization();
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }

    private async Task SetShellNewOrderLockAsync(bool oldValue, bool newValue)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _backendClient.SetShellNewOrderLockAsync(newValue, Guid.NewGuid(), cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _suppressShellNewLockSync = true;
            try
            {
                IsShellNewOrderLocked = oldValue;
            }
            finally
            {
                _suppressShellNewLockSync = false;
            }

            StatusText = ex.Message;
        }
    }

    private async Task SetDefaultDropEffectAsync(DefaultDropEffect oldValue, DefaultDropEffect newValue)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _backendClient.UpdateSpecialMenuItemAsync(
                new PipeRequest
                {
                    SpecialKind = Kind,
                    DefaultDropEffect = newValue,
                    ClientOperationId = Guid.NewGuid()
                },
                cts.Token);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _suppressDropEffectSync = true;
            try
            {
                SelectedDropEffect = oldValue;
            }
            finally
            {
                _suppressDropEffectSync = false;
            }

            await FrontendMessageBox.ShowErrorAsync(ex.Message, Title);
        }
    }

    private void UpdatePageStateFromSnapshot(IEnumerable<SpecialMenuEntry> snapshot)
    {
        if (Kind == SpecialMenuKind.ShellNew)
        {
            var lockedText = snapshot.Select(item => item.Metadata.GetValueOrDefault("OrderLocked")).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            _suppressShellNewLockSync = true;
            try
            {
                IsShellNewOrderLocked = bool.TryParse(lockedText, out var locked) && locked;
            }
            finally
            {
                _suppressShellNewLockSync = false;
            }
        }

        if (Kind == SpecialMenuKind.DragDrop)
        {
            var effectText = snapshot.FirstOrDefault(static item => item.Metadata.GetValueOrDefault("EntryType") == "DefaultDropEffect")?.Notes;
            _suppressDropEffectSync = true;
            try
            {
                SelectedDropEffect = Enum.TryParse<DefaultDropEffect>(effectText, out var effect) ? effect : DefaultDropEffect.Default;
            }
            finally
            {
                _suppressDropEffectSync = false;
            }
        }
    }

    private void RebuildWinXGroups()
    {
        if (Kind != SpecialMenuKind.WinX)
        {
            return;
        }

        foreach (var group in WinXGroups)
        {
            _winXExpandedStates[group.Group.KeyName] = group.IsExpanded;
        }

        WinXGroups.Clear();
        WinXGroupNodeViewModel? currentGroup = null;
        foreach (var item in Items)
        {
            if (item.Entry.Metadata.GetValueOrDefault("EntryType") == "Group")
            {
                currentGroup = CreateWinXGroupNode(item);
                WinXGroups.Add(currentGroup);
                continue;
            }

            if (currentGroup is null || !string.Equals(currentGroup.Group.KeyName, item.Entry.GroupName, StringComparison.OrdinalIgnoreCase))
            {
                currentGroup = WinXGroups.FirstOrDefault(group => string.Equals(group.Group.KeyName, item.Entry.GroupName, StringComparison.OrdinalIgnoreCase));
                if (currentGroup is null)
                {
                    currentGroup = CreateWinXGroupNode(new SpecialMenuItemViewModel(
                        new SpecialMenuEntry
                        {
                            Id = $"{SpecialMenuKind.WinX}:Synthetic:{item.Entry.GroupName}",
                            Kind = SpecialMenuKind.WinX,
                            DisplayName = item.Entry.GroupName ?? string.Empty,
                            KeyName = item.Entry.GroupName ?? string.Empty,
                            IsEnabled = true,
                            CanEdit = false,
                            CanDelete = false,
                            Metadata = new Dictionary<string, string> { ["EntryType"] = "Group" }
                        },
                        _iconPreviewService,
                        _localization,
                        SetEnabledAsync));
                    WinXGroups.Add(currentGroup);
                }
            }

            currentGroup.Items.Add(item);
        }

        UpdateWinXMoveAvailability();
    }

    private void UpdateShellNewMoveAvailability()
    {
        if (Kind != SpecialMenuKind.ShellNew)
        {
            return;
        }

        var movableItems = Items
            .Where(static item => item.Entry.CanMove)
            .ToArray();

        for (var index = 0; index < movableItems.Length; index++)
        {
            movableItems[index].SetMoveAvailability(
                IsShellNewOrderLocked && index > 0,
                IsShellNewOrderLocked && index < movableItems.Length - 1);
        }

        foreach (var item in Items.Where(static item => !item.Entry.CanMove))
        {
            item.SetMoveAvailability(false, false);
        }
    }

    private static bool IsExpectedShellNewMoveFailure(Exception exception)
    {
        var message = exception.Message;
        return message.Contains("ShellNew ordering", StringComparison.OrdinalIgnoreCase)
            || message.Contains(@"Discardable\PostSetup\ShellNew", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Access to the registry key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("registry key is denied", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateWinXMoveAvailability()
    {
        for (var groupIndex = 0; groupIndex < WinXGroups.Count; groupIndex++)
        {
            var group = WinXGroups[groupIndex];
            var entries = group.Items
                .Where(static item => item.Entry.Metadata.GetValueOrDefault("EntryType") == "Entry")
                .ToArray();

            for (var itemIndex = 0; itemIndex < entries.Length; itemIndex++)
            {
                var hasItemAboveInGroup = itemIndex > 0;
                var hasItemBelowInGroup = itemIndex < entries.Length - 1;
                var hasGroupAbove = HasAdjacentWinXGroup(groupIndex, moveUp: true);
                var hasGroupBelow = HasAdjacentWinXGroup(groupIndex, moveUp: false);

                entries[itemIndex].SetMoveAvailability(
                    hasItemAboveInGroup || hasGroupAbove,
                    hasItemBelowInGroup || hasGroupBelow);
            }
        }
    }

    private bool HasAdjacentWinXGroup(int groupIndex, bool moveUp)
    {
        var nextIndex = moveUp ? groupIndex - 1 : groupIndex + 1;
        return nextIndex >= 0 && nextIndex < WinXGroups.Count;
    }

    private WinXGroupNodeViewModel CreateWinXGroupNode(SpecialMenuItemViewModel group)
    {
        var node = new WinXGroupNodeViewModel(group)
        {
            IsExpanded = !_winXExpandedStates.TryGetValue(group.KeyName, out var expanded) || expanded
        };
        return node;
    }

    private static string[]? SplitParts(string? value, int count, bool allowEmptyMiddleParts = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('|');
        if (parts.Length < count || (!allowEmptyMiddleParts && parts.Take(count - 1).Any(string.IsNullOrWhiteSpace)))
        {
            return null;
        }

        return parts.Concat(Enumerable.Repeat(string.Empty, count)).Take(count).Select(static part => part.Trim()).ToArray();
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _backendClient.NotificationReceived -= OnNotificationReceived;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}

public sealed class ShellNewPageViewModel : SpecialMenuPageViewModel
{
    public ShellNewPageViewModel(IBackendClient backendClient, IconPreviewService iconPreviewService, LocalizationService localization, ExplorerRestartStateService explorerRestartState)
        : base(SpecialMenuKind.ShellNew, "ShellNewPageTitle", "ShellNewPageDescription", backendClient, iconPreviewService, localization, explorerRestartState)
    {
    }
}

public sealed class SendToPageViewModel : SpecialMenuPageViewModel
{
    public SendToPageViewModel(IBackendClient backendClient, IconPreviewService iconPreviewService, LocalizationService localization, ExplorerRestartStateService explorerRestartState)
        : base(SpecialMenuKind.SendTo, "SendToPageTitle", "SendToPageDescription", backendClient, iconPreviewService, localization, explorerRestartState)
    {
    }
}

public sealed class WinXPageViewModel : SpecialMenuPageViewModel
{
    public WinXPageViewModel(IBackendClient backendClient, IconPreviewService iconPreviewService, LocalizationService localization, ExplorerRestartStateService explorerRestartState)
        : base(SpecialMenuKind.WinX, "WinXPageTitle", "WinXPageDescription", backendClient, iconPreviewService, localization, explorerRestartState)
    {
    }
}

public sealed partial class WinXGroupNodeViewModel : ObservableObject
{
    public WinXGroupNodeViewModel(SpecialMenuItemViewModel group)
    {
        Group = group;
    }

    public SpecialMenuItemViewModel Group { get; }

    public ObservableCollection<SpecialMenuItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}
