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
        LocalizationService localization)
    {
        Kind = kind;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _backendClient = backendClient;
        _iconPreviewService = iconPreviewService;
        _localization = localization;
        ItemsView = new ListCollectionView(Items);
        ItemsView.Filter = FilterItem;
        SearchLabel = _localization.Translate("SearchLabel");
        AddText = _localization.Translate("Add");
        EditText = _localization.Translate("Edit");
        RefreshText = _localization.Translate("Refresh");
        RestoreDefaultsText = _localization.Translate("RestoreDefault");
        LockNewMenuText = _localization.Translate("LockNewMenu");
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
            await _backendClient.DeleteSpecialMenuItemAsync(item.Entry, Guid.NewGuid(), cts.Token);
            Items.Remove(item);
            if (Kind == SpecialMenuKind.WinX)
            {
                await RefreshAsync();
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

        try
        {
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
                await RefreshAsync();
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

    private async Task<bool> SetEnabledAsync(SpecialMenuItemViewModel item, bool enabled)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var updated = await _backendClient.SetSpecialMenuItemEnabledAsync(item.Entry, enabled, Guid.NewGuid(), cts.Token);
            if (updated is not null)
            {
                item.Update(updated);
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
                var extension = await TextInputDialog.ShowAsync(Title, "Extension", ".txt");
                return string.IsNullOrWhiteSpace(extension)
                    ? null
                    : new PipeRequest { SpecialKind = Kind, ShellNewCreate = new ShellNewCreateRequest(extension), ClientOperationId = operationId };
            case SpecialMenuKind.SendTo:
                var sendTo = await TextInputDialog.ShowAsync(Title, "Name|Target|Arguments", "Notepad|notepad.exe|");
                var sendToParts = SplitParts(sendTo, 3);
                return sendToParts is null
                    ? null
                    : new PipeRequest { SpecialKind = Kind, SendToCreate = new SendToCreateRequest(sendToParts[0], sendToParts[1], sendToParts[2]), ClientOperationId = operationId };
            case SpecialMenuKind.WinX:
                var winx = await TextInputDialog.ShowAsync(Title, "Name|Target|Group|Arguments OR Group:name", "Terminal|wt.exe|Group3|");
                if (string.IsNullOrWhiteSpace(winx))
                {
                    return null;
                }

                if (winx.StartsWith("Group:", StringComparison.OrdinalIgnoreCase))
                {
                    return new PipeRequest { SpecialKind = Kind, WinXCreateGroup = new WinXCreateGroupRequest(winx[6..].Trim()), ClientOperationId = operationId };
                }

                var winxParts = SplitParts(winx, 4);
                return winxParts is null
                    ? null
                    : new PipeRequest { SpecialKind = Kind, WinXCreateEntry = new WinXCreateEntryRequest(winxParts[0], winxParts[1], winxParts[2], winxParts[3]), ClientOperationId = operationId };
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
                var shellNew = await TextInputDialog.ShowAsync(Title, "Name|Icon|Command|Data|BeforeSeparator(true/false)", $"{item.DisplayName}|{item.Entry.IconPath}|{item.Entry.CommandText}||");
                var shellNewParts = SplitParts(shellNew, 5, allowEmptyMiddleParts: true);
                return shellNewParts is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        ShellNewUpdate = new ShellNewUpdateRequest(
                            item.Id,
                            EmptyToNull(shellNewParts[0]),
                            EmptyToNull(shellNewParts[1]),
                            EmptyToNull(shellNewParts[2]),
                            EmptyToNull(shellNewParts[3]),
                            bool.TryParse(shellNewParts[4], out var beforeSeparator) ? beforeSeparator : (bool?)null),
                        ClientOperationId = operationId
                    };
            case SpecialMenuKind.SendTo:
                var sendTo = await TextInputDialog.ShowAsync(Title, "Name|Target|Arguments|WorkingDirectory|Icon|RunAsAdmin(true/false)", $"{item.DisplayName}|{item.Entry.TargetPath}|{item.Entry.Arguments}|{item.Entry.WorkingDirectory}|{item.Entry.IconPath}|");
                var sendToParts = SplitParts(sendTo, 6, allowEmptyMiddleParts: true);
                return sendToParts is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        SendToUpdate = new SendToUpdateRequest(
                            item.Id,
                            EmptyToNull(sendToParts[0]),
                            EmptyToNull(sendToParts[1]),
                            EmptyToNull(sendToParts[2]),
                            EmptyToNull(sendToParts[3]),
                            EmptyToNull(sendToParts[4]),
                            bool.TryParse(sendToParts[5], out var sendToAdmin) ? sendToAdmin : (bool?)null),
                        ClientOperationId = operationId
                    };
            case SpecialMenuKind.WinX:
                var winx = await TextInputDialog.ShowAsync(Title, "Name|Target|Group|Arguments|WorkingDirectory|RunAsAdmin(true/false)", $"{item.DisplayName}|{item.Entry.TargetPath}|{item.Entry.GroupName}|{item.Entry.Arguments}|{item.Entry.WorkingDirectory}|");
                var winxParts = SplitParts(winx, 6, allowEmptyMiddleParts: true);
                return winxParts is null
                    ? null
                    : new PipeRequest
                    {
                        SpecialKind = Kind,
                        WinXUpdateEntry = new WinXUpdateEntryRequest(
                            item.Id,
                            EmptyToNull(winxParts[0]),
                            EmptyToNull(winxParts[1]),
                            EmptyToNull(winxParts[3]),
                            EmptyToNull(winxParts[4]),
                            EmptyToNull(winxParts[2]),
                            bool.TryParse(winxParts[5], out var winxAdmin) ? winxAdmin : (bool?)null),
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
        var existing = Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
        {
            if (existing.Remove(entry.Id, out var current))
            {
                current.Update(entry);
            }
            else
            {
                Items.Add(new SpecialMenuItemViewModel(entry, _iconPreviewService, _localization, SetEnabledAsync));
            }
        }

        foreach (var stale in existing.Values)
        {
            Items.Remove(stale);
        }

        UpdatePageStateFromSnapshot(snapshot);
        RebuildWinXGroups();
        ItemsView.Refresh();
    }

    private void Upsert(SpecialMenuEntry entry)
    {
        var current = Items.FirstOrDefault(item => string.Equals(item.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            Items.Add(new SpecialMenuItemViewModel(entry, _iconPreviewService, _localization, SetEnabledAsync));
        }
        else
        {
            current.Update(entry);
        }

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
        if (notification.SpecialKind == Kind && notification.SpecialItem is not null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => Upsert(notification.SpecialItem));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        SearchLabel = _localization.Translate("SearchLabel");
        AddText = _localization.Translate("Add");
        EditText = _localization.Translate("Edit");
        RefreshText = _localization.Translate("Refresh");
        RestoreDefaultsText = _localization.Translate("RestoreDefault");
        LockNewMenuText = _localization.Translate("LockNewMenu");
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

            await FrontendMessageBox.ShowErrorAsync(ex.Message, Title);
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
        var flatItems = Items
            .Where(static item => item.Entry.Metadata.GetValueOrDefault("EntryType") == "Entry")
            .ToArray();
        for (var index = 0; index < flatItems.Length; index++)
        {
            flatItems[index].SetMoveAvailability(index > 0, index < flatItems.Length - 1);
        }

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
    public ShellNewPageViewModel(IBackendClient backendClient, IconPreviewService iconPreviewService, LocalizationService localization)
        : base(SpecialMenuKind.ShellNew, "ShellNewPageTitle", "ShellNewPageDescription", backendClient, iconPreviewService, localization)
    {
    }
}

public sealed class SendToPageViewModel : SpecialMenuPageViewModel
{
    public SendToPageViewModel(IBackendClient backendClient, IconPreviewService iconPreviewService, LocalizationService localization)
        : base(SpecialMenuKind.SendTo, "SendToPageTitle", "SendToPageDescription", backendClient, iconPreviewService, localization)
    {
    }
}

public sealed class WinXPageViewModel : SpecialMenuPageViewModel
{
    public WinXPageViewModel(IBackendClient backendClient, IconPreviewService iconPreviewService, LocalizationService localization)
        : base(SpecialMenuKind.WinX, "WinXPageTitle", "WinXPageDescription", backendClient, iconPreviewService, localization)
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
