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
        RebuildGroups();
        ApplyPendingNavigationRequest();
    }

    public ObservableCollection<ApplicationGroupViewModel> Groups { get; } = [];

    public string Title => _localization.Translate("ApplicationGroupsPageTitle");
    public string Description => _localization.Translate("ApplicationGroupsPageDescription");
    public string SearchLabel => _localization.Translate("SearchLabel");

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ScrollTargetItemId { get; private set; }

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    public void AcknowledgeScrollTarget(string itemId)
    {
        if (string.Equals(ScrollTargetItemId, itemId, StringComparison.OrdinalIgnoreCase))
        {
            ScrollTargetItemId = null;
        }
    }

    private void RebuildGroups()
    {
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
            RebuildGroups();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SearchLabel));
        RebuildGroups();
    }

    private void OnNavigationFilterRequested(object? sender, GlobalSearchFilterRequestedEventArgs e)
    {
        if (e.TargetPageType != typeof(ContextMenuMgr.Frontend.Views.Pages.ApplicationGroupsPage))
        {
            return;
        }

        ApplyNavigationRequest(e.ItemId);
        _navigationFilterService.ConsumePendingRequest(e.TargetPageType);
    }

    private void ApplyPendingNavigationRequest()
    {
        var pending = _navigationFilterService.ConsumePendingRequest(
            typeof(ContextMenuMgr.Frontend.Views.Pages.ApplicationGroupsPage));
        if (pending is not null)
        {
            ApplyNavigationRequest(pending.ItemId);
        }
    }

    private void ApplyNavigationRequest(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        SearchText = string.Empty;
        RebuildGroups();
        ScrollTargetItemId = itemId;
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
