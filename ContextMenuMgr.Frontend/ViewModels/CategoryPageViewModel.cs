using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMenuMgr.Contracts;
using ContextMenuMgr.Frontend.Services;

namespace ContextMenuMgr.Frontend.ViewModels;

/// <summary>
/// Represents the category Page View Model.
/// </summary>
public partial class CategoryPageViewModel : ObservableObject, IDisposable
{
    private readonly ContextMenuWorkspaceService _workspace;
    private readonly LocalizationService _localization;
    private readonly FrontendSettingsService _settingsService;
    private readonly ListPlaceholderDebugStateService _placeholderDebug;
    private readonly HashSet<string> _loggedDesktopCompatibilityItemIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="CategoryPageViewModel"/> class.
    /// </summary>
    public CategoryPageViewModel(
        ContextMenuCategory category,
        ContextMenuWorkspaceService workspace,
        LocalizationService localization,
        FrontendSettingsService settingsService,
        ListPlaceholderDebugStateService placeholderDebug)
    {
        Category = category;
        _workspace = workspace;
        _localization = localization;
        _settingsService = settingsService;
        _placeholderDebug = placeholderDebug;
        _localization.LanguageChanged += OnLanguageChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _placeholderDebug.PropertyChanged += OnPlaceholderDebugPropertyChanged;
        _workspace.Items.CollectionChanged += OnItemsCollectionChanged;
        foreach (var item in _workspace.Items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        ItemsView = new ListCollectionView(_workspace.Items);
        ItemsView.Filter = FilterItem;
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(ContextMenuItemViewModel.SortAttentionWeight), ListSortDirection.Ascending));
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(ContextMenuItemViewModel.SortDeletedWeight), ListSortDirection.Ascending));
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(ContextMenuItemViewModel.DisplayName), ListSortDirection.Ascending));

        RefreshLocalizedText();
        RefreshListPlaceholderState();
    }

    /// <summary>
    /// Gets the category.
    /// </summary>
    public ContextMenuCategory Category { get; }

    /// <summary>
    /// Gets the items View.
    /// </summary>
    public ICollectionView ItemsView { get; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    [ObservableProperty]
    public partial string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the search Text.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public string PermanentDeleteText => _localization.Translate("PermanentDelete");

    public string RegistryMissingText => _localization.Translate("RegistryMissingText");

    public string SearchLabel => _localization.Translate("SearchLabel");

    public string DeleteText => _localization.Translate("Delete");

    public string CancelText => _localization.Translate("DialogCancel");

    public string LoadingItemsText => _localization.Translate("LoadingItemsText");

    public string EmptyItemsText => _localization.Translate("EmptyItemsText");

    public bool IsListLoading => _placeholderDebug.ForceLoadingState || _workspace.IsLoading;

    public bool IsListEmpty
    {
        get
        {
            if (IsListLoading)
            {
                return false;
            }

            if (_placeholderDebug.ForceEmptyState)
            {
                return true;
            }

            return !ItemsView.Cast<object>().Any();
        }
    }

    public bool ShowListPlaceholder => IsListLoading || IsListEmpty;

    [RelayCommand]
    private Task DeleteOrUndoAsync(ContextMenuItemViewModel? item)
    {
        return item is null ? Task.CompletedTask : _workspace.DeleteOrUndoAsync(item);
    }

    [RelayCommand]
    private Task OpenPermanentDeleteFlyoutAsync(ContextMenuItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsPermanentDeleteFlyoutOpen = true;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ConfirmPermanentDeleteAsync(ContextMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsPermanentDeleteFlyoutOpen = false;
        await _workspace.PermanentlyDeleteAsync(item);
    }

    private bool FilterItem(object obj)
    {
        if (obj is not ContextMenuItemViewModel item || !IsItemVisibleInCurrentCategory(item))
        {
            return false;
        }

        if (item.IsWindows11ContextMenu)
        {
            return false;
        }

        if (_settingsService.Current.HideDisabledItems && !item.IsEnabled && !item.IsDeleted)
        {
            return false;
        }

        return MatchesSearch(item);
    }

    private bool IsItemVisibleInCurrentCategory(ContextMenuItemViewModel item)
    {
        if (item.Category == Category)
        {
            return true;
        }

        if (Category == ContextMenuCategory.DesktopBackground
            && item.Category == ContextMenuCategory.DirectoryBackground
            && IsDirectoryBackgroundItemVisibleOnDesktopBackgroundPage(item))
        {
            LogDesktopBackgroundCompatibilityItemVisible(item);
            return true;
        }

        return false;
    }

    private static bool IsDirectoryBackgroundItemVisibleOnDesktopBackgroundPage(ContextMenuItemViewModel item)
    {
        return item.Entry.SourceRootPath.StartsWith(@"Directory\Background\", StringComparison.OrdinalIgnoreCase)
               || item.Entry.RegistryPath.StartsWith(@"Directory\Background\", StringComparison.OrdinalIgnoreCase)
               || item.Entry.BackendRegistryPath.Contains(@"\Directory\Background\", StringComparison.OrdinalIgnoreCase);
    }

    private void LogDesktopBackgroundCompatibilityItemVisible(ContextMenuItemViewModel item)
    {
        if (!ShouldLogDesktopBackgroundCompatibilityItem(item)
            || !_loggedDesktopCompatibilityItemIds.Add(item.Id))
        {
            return;
        }

        FrontendDebugLog.Info(
            nameof(CategoryPageViewModel),
            "DesktopBackgroundCompatibilityItemVisible: "
            + $"DisplayName={item.DisplayName}, "
            + $"KeyName={item.KeyName}, "
            + $"ItemId={item.Id}, "
            + $"SourceCategory={item.Category}, "
            + $"VisibleCategory={Category}, "
            + $"RegistryPath={item.RegistryPath}, "
            + $"HandlerClsid={item.Entry.HandlerClsid ?? "<null>"}.");
    }

    private static bool ShouldLogDesktopBackgroundCompatibilityItem(ContextMenuItemViewModel item)
    {
        return ContainsNvidiaSignal(item.DisplayName)
               || ContainsNvidiaSignal(item.KeyName)
               || ContainsNvidiaSignal(item.Entry.HandlerClsid)
               || ContainsNvidiaSignal(item.Entry.FilePath);
    }

    private static bool ContainsNvidiaSignal(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && (value.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("Nv", StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSearchTextChanged(string value)
    {
        ItemsView.Refresh();
        RefreshListPlaceholderState();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedText();
        OnPropertyChanged(nameof(PermanentDeleteText));
        OnPropertyChanged(nameof(RegistryMissingText));
        OnPropertyChanged(nameof(SearchLabel));
        OnPropertyChanged(nameof(DeleteText));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(LoadingItemsText));
        OnPropertyChanged(nameof(EmptyItemsText));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ItemsView.Refresh();
        RefreshListPlaceholderState();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContextMenuWorkspaceService.IsLoading))
        {
            RefreshListPlaceholderState();
        }
    }

    private void OnPlaceholderDebugPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListPlaceholderDebugStateService.Mode)
            or nameof(ListPlaceholderDebugStateService.ForceLoadingState)
            or nameof(ListPlaceholderDebugStateService.ForceEmptyState)
            or nameof(ListPlaceholderDebugStateService.HasForcedState))
        {
            RefreshListPlaceholderState();
        }
    }

    private void RefreshLocalizedText()
    {
        var (nameKey, descriptionKey) = ContextMenuCategoryText.GetResourceKeys(Category);

        Title = _localization.Translate(nameKey);
        Description = _localization.Translate(descriptionKey);
    }

    private bool MatchesSearch(ContextMenuItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return Contains(item.DisplayName, search)
               || Contains(item.KeyName, search)
               || Contains(item.Subtitle, search)
               || Contains(item.RegistryPath, search)
               || Contains(item.ShellPathTail, search)
               || Contains(item.Entry.HandlerClsid, search)
               || Contains(item.Entry.FilePath, search)
               || Contains(item.Entry.CommandText, search)
               || Contains(item.Notes, search);
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        ItemsView.Refresh();
        RefreshListPlaceholderState();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContextMenuItemViewModel.DisplayName)
            or nameof(ContextMenuItemViewModel.KeyName)
            or nameof(ContextMenuItemViewModel.RegistryPath)
            or nameof(ContextMenuItemViewModel.Subtitle)
            or nameof(ContextMenuItemViewModel.Notes)
            or nameof(ContextMenuItemViewModel.IsEnabled)
            or nameof(ContextMenuItemViewModel.IsWindows11ContextMenu)
            or nameof(ContextMenuItemViewModel.IsDeleted)
            or nameof(ContextMenuItemViewModel.HasDetectedChange)
            or nameof(ContextMenuItemViewModel.IsPendingApproval)
            or nameof(ContextMenuItemViewModel.HasConsistencyIssue))
        {
            ItemsView.Refresh();
            RefreshListPlaceholderState();
        }
    }

    private void RefreshListPlaceholderState()
    {
        OnPropertyChanged(nameof(IsListLoading));
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(ShowListPlaceholder));
    }

    /// <summary>
    /// Executes dispose.
    /// </summary>
    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        _placeholderDebug.PropertyChanged -= OnPlaceholderDebugPropertyChanged;
        _workspace.Items.CollectionChanged -= OnItemsCollectionChanged;
        foreach (var item in _workspace.Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
    }
}
