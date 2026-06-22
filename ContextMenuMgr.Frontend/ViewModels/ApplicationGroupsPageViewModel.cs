using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuMgr.Frontend.Services;

namespace ContextMenuMgr.Frontend.ViewModels;

public partial class ApplicationGroupsPageViewModel : ObservableObject, IDisposable
{
    private static readonly Regex CommandPathRegex = new(
        "^(?:\\\"(?<path>[^\\\"]+\\.(?:exe|dll))\\\"|(?<path>[^\\s]+\\.(?:exe|dll)))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly ContextMenuWorkspaceService _workspace;
    private readonly LocalizationService _localization;
    private readonly GlobalSearchNavigationFilterService _navigationFilterService;
    private bool _applyingNavigationFilter;

    public ApplicationGroupsPageViewModel(
        ContextMenuWorkspaceService workspace,
        LocalizationService localization,
        GlobalSearchNavigationFilterService navigationFilterService)
    {
        _workspace = workspace;
        _localization = localization;
        _navigationFilterService = navigationFilterService;
        _workspace.Items.CollectionChanged += OnItemsChanged;
        foreach (var item in _workspace.Items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }
        _localization.LanguageChanged += OnLanguageChanged;
        _navigationFilterService.FilterRequested += OnNavigationFilterRequested;
        if (!ApplyPendingNavigationRequest())
        {
            RebuildGroups();
        }
    }

    public ObservableCollection<ApplicationGroupViewModel> Groups { get; } = [];

    public string Title => _localization.Translate("ApplicationGroupsPageTitle");
    public string Description => _localization.Translate("ApplicationGroupsPageDescription");
    public string SearchLabel => _localization.Translate("SearchLabel");
    public string NavigationFilterBannerText => _localization.Translate("ApplicationGroupFilterActive");
    public string ClearFilterText => _localization.Translate("ClearApplicationGroupFilter");

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NavigationFocusItemId { get; private set; }

    [ObservableProperty]
    public partial string? NavigationFocusGroupIdentity { get; private set; }

    [ObservableProperty]
    public partial bool IsNavigationFilterActive { get; private set; }

    partial void OnSearchTextChanged(string value)
    {
        if (_applyingNavigationFilter)
        {
            return;
        }

        ClearNavigationFilterState();
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        if (IsNavigationFilterActive
            && !string.IsNullOrWhiteSpace(NavigationFocusItemId)
            && !string.IsNullOrWhiteSpace(NavigationFocusGroupIdentity))
        {
            var targetItem = _workspace.Items
                .FirstOrDefault(item => !item.IsWindows11ContextMenu
                    && IsClassicCategory(item.Category)
                    && string.Equals(item.Id, NavigationFocusItemId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GetGroupIdentity(item), NavigationFocusGroupIdentity, StringComparison.OrdinalIgnoreCase));

            if (targetItem is not null)
            {
                Groups.Clear();
                Groups.Add(new ApplicationGroupViewModel(
                    NavigationFocusGroupIdentity,
                    [targetItem],
                    _workspace,
                    _localization));
                return;
            }

            FrontendDebugLog.Warning(
                nameof(ApplicationGroupsPageViewModel),
                "ApplicationGroupNavigationFilterTargetMissing: "
                + $"ItemId={NavigationFocusItemId}, "
                + $"GroupIdentity={NavigationFocusGroupIdentity}.");
            ClearNavigationFilterState();
        }

        var groups = _workspace.Items
            .Where(static item => !item.IsWindows11ContextMenu && IsClassicCategory(item.Category))
            .GroupBy(GetGroupIdentity, StringComparer.OrdinalIgnoreCase)
            .Where(group => MatchesGroup(group, SearchText))
            .OrderBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ApplicationGroupViewModel(group.Key, group.ToArray(), _workspace, _localization))
            .ToArray();

        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }
    }

    private static string GetGroupIdentity(ContextMenuItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.Entry.FilePath))
        {
            return item.Entry.FilePath.Trim();
        }

        var commandMatch = CommandPathRegex.Match(item.Entry.CommandText?.Trim() ?? string.Empty);
        if (commandMatch.Success)
        {
            return commandMatch.Groups["path"].Value;
        }

        if (!string.IsNullOrWhiteSpace(item.Entry.HandlerClsid))
        {
            return item.Entry.HandlerClsid.Trim();
        }

        return !string.IsNullOrWhiteSpace(item.Entry.RegistryPath)
            ? item.Entry.RegistryPath.Trim()
            : item.Id;
    }

    private static bool IsClassicCategory(ContextMenuMgr.Contracts.ContextMenuCategory category) => category is
        ContextMenuMgr.Contracts.ContextMenuCategory.File
        or ContextMenuMgr.Contracts.ContextMenuCategory.AllFileSystemObjects
        or ContextMenuMgr.Contracts.ContextMenuCategory.Folder
        or ContextMenuMgr.Contracts.ContextMenuCategory.Directory
        or ContextMenuMgr.Contracts.ContextMenuCategory.DirectoryBackground
        or ContextMenuMgr.Contracts.ContextMenuCategory.DesktopBackground
        or ContextMenuMgr.Contracts.ContextMenuCategory.Drive
        or ContextMenuMgr.Contracts.ContextMenuCategory.Library
        or ContextMenuMgr.Contracts.ContextMenuCategory.Computer
        or ContextMenuMgr.Contracts.ContextMenuCategory.RecycleBin;

    private static bool MatchesGroup(IEnumerable<ContextMenuItemViewModel> group, string query)
    {
        var items = group.ToArray();
        var fields = new List<string?> { GetGroupIdentity(items[0]) };
        fields.AddRange(items.SelectMany(item => new[]
        {
            item.DisplayName,
            item.CategoryName,
            item.StateLabel,
            item.RegistryPath,
            item.UserNote,
            item.Entry.FilePath,
            item.Entry.HandlerClsid,
            item.Entry.CommandText
        }));
        return ContextMenuSearchMatcher.MatchesFields(query, fields.ToArray());
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ContextMenuItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ContextMenuItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        RebuildGroups();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContextMenuItemViewModel.UserNote)
            or nameof(ContextMenuItemViewModel.IsEnabled)
            or nameof(ContextMenuItemViewModel.DisplayName))
        {
            if (sender is ContextMenuItemViewModel item
                && e.PropertyName == nameof(ContextMenuItemViewModel.DisplayName)
                && IsNavigationFilterActive
                && string.Equals(item.Id, NavigationFocusItemId, StringComparison.OrdinalIgnoreCase))
            {
                SetSearchTextFromNavigationFilter(item.DisplayName);
            }

            RebuildGroups();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SearchLabel));
        OnPropertyChanged(nameof(NavigationFilterBannerText));
        OnPropertyChanged(nameof(ClearFilterText));
        RebuildGroups();
    }

    private void OnNavigationFilterRequested(object? sender, GlobalSearchFilterRequestedEventArgs e)
    {
        if (e.TargetPageType != typeof(ContextMenuMgr.Frontend.Views.Pages.ApplicationGroupsPage))
        {
            return;
        }

        if (ApplyNavigationRequest(e.ItemId, e.FilterText))
        {
            _navigationFilterService.ConsumePendingRequest(e.TargetPageType);
        }
    }

    private bool ApplyPendingNavigationRequest()
    {
        var pending = _navigationFilterService.ConsumePendingRequest(
            typeof(ContextMenuMgr.Frontend.Views.Pages.ApplicationGroupsPage));
        if (pending is null)
        {
            return false;
        }

        return ApplyNavigationRequest(pending.ItemId, pending.FilterText);
    }

    private bool ApplyNavigationRequest(string? itemId, string? fallbackFilterText)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        var targetItem = _workspace.Items.FirstOrDefault(item =>
            !item.IsWindows11ContextMenu
            && IsClassicCategory(item.Category)
            && string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase));

        if (targetItem is null)
        {
            FrontendDebugLog.Warning(
                nameof(ApplicationGroupsPageViewModel),
                $"ApplicationGroupNavigationFilterItemNotFound: ItemId={itemId}.");

            ClearNavigationFilterState();
            SetSearchTextFromNavigationFilter(string.IsNullOrWhiteSpace(fallbackFilterText)
                ? itemId
                : fallbackFilterText);

            RebuildGroups();
            return true;
        }

        var groupIdentity = GetGroupIdentity(targetItem);
        NavigationFocusItemId = targetItem.Id;
        NavigationFocusGroupIdentity = groupIdentity;
        IsNavigationFilterActive = true;

        SetSearchTextFromNavigationFilter(targetItem.DisplayName);

        RebuildGroups();

        FrontendDebugLog.Info(
            nameof(ApplicationGroupsPageViewModel),
            "ApplicationGroupNavigationFilterApplied: "
            + $"ItemId={targetItem.Id}, "
            + $"DisplayName='{SanitizeLogText(targetItem.DisplayName)}', "
            + $"GroupIdentity='{SanitizeLogText(groupIdentity)}'.");
        return true;
    }

    [RelayCommand]
    private void ClearNavigationFilter()
    {
        ClearNavigationFilterState();
        SearchText = string.Empty;
        RebuildGroups();
    }

    private void SetSearchTextFromNavigationFilter(string text)
    {
        _applyingNavigationFilter = true;
        try
        {
            SearchText = text;
        }
        finally
        {
            _applyingNavigationFilter = false;
        }
    }

    private void ClearNavigationFilterState()
    {
        NavigationFocusItemId = null;
        NavigationFocusGroupIdentity = null;
        IsNavigationFilterActive = false;
    }

    private static string SanitizeLogText(string? text)
    {
        return (text ?? string.Empty)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _workspace.Items.CollectionChanged -= OnItemsChanged;
        foreach (var item in _workspace.Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
        _localization.LanguageChanged -= OnLanguageChanged;
        _navigationFilterService.FilterRequested -= OnNavigationFilterRequested;
    }
}

public partial class ApplicationGroupViewModel : ObservableObject
{
    private readonly ContextMenuWorkspaceService _workspace;
    private readonly LocalizationService _localization;

    public ApplicationGroupViewModel(
        string identity,
        IReadOnlyList<ContextMenuItemViewModel> items,
        ContextMenuWorkspaceService workspace,
        LocalizationService localization)
    {
        Identity = identity;
        Items = items;
        _workspace = workspace;
        _localization = localization;
    }

    public string Identity { get; }
    public IReadOnlyList<ContextMenuItemViewModel> Items { get; }
    public string CountText => _localization.Format("ApplicationGroupCountFormat", Items.Count);
    public string DisableAllText => _localization.Translate(IsDisabling ? "ApplicationGroupDisabling" : "ApplicationGroupDisableAll");
    public bool CanDisableAll => !IsDisabling && Items.Any(static item => item.IsEnabled && item.CanToggle);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisableAllText))]
    [NotifyPropertyChangedFor(nameof(CanDisableAll))]
    public partial bool IsDisabling { get; private set; }

    [RelayCommand(CanExecute = nameof(CanDisableAll))]
    private async Task DisableAllAsync()
    {
        IsDisabling = true;
        DisableAllCommand.NotifyCanExecuteChanged();
        try
        {
            foreach (var item in Items.Where(static item => item.IsEnabled && item.CanToggle).ToArray())
            {
                await _workspace.SetEnabledAsync(item, false);
            }
        }
        finally
        {
            IsDisabling = false;
            OnPropertyChanged(nameof(CanDisableAll));
            DisableAllCommand.NotifyCanExecuteChanged();
        }
    }
}
