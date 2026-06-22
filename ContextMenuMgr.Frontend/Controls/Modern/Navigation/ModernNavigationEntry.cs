using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Controls.Modern.Navigation;

public sealed class ModernNavigationEntry : INotifyPropertyChanged, IDisposable
{
    private static readonly DependencyPropertyDescriptor? ContentDescriptor =
        DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(NavigationViewItem));
    private static readonly DependencyPropertyDescriptor? IconDescriptor =
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.IconProperty, typeof(NavigationViewItem));
    private static readonly DependencyPropertyDescriptor? BadgeDescriptor =
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.InfoBadgeProperty, typeof(NavigationViewItem));
    private static readonly DependencyPropertyDescriptor? VisibilityDescriptor =
        DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(NavigationViewItem));
    private static readonly DependencyPropertyDescriptor? EnabledDescriptor =
        DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty, typeof(NavigationViewItem));
    private static readonly DependencyPropertyDescriptor? ExpandedDescriptor =
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.IsExpandedProperty, typeof(NavigationViewItem));
    private static readonly DependencyPropertyDescriptor? ItemsSourceDescriptor =
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.MenuItemsSourceProperty, typeof(NavigationViewItem));

    private bool _isSelected;
    private bool _isActiveBranch;
    private INotifyCollectionChanged? _itemsSourceCollection;

    internal ModernNavigationEntry(object sourceItem, ModernNavigationEntry? parent, int depth, bool isFooter)
    {
        SourceItem = sourceItem;
        SourceNavigationViewItem = sourceItem as NavigationViewItem;
        Parent = parent;
        Depth = depth;
        IsFooter = isFooter;
        RefreshFromSource();
        AttachSourceWatchers();
    }

    public object SourceItem { get; }

    public NavigationViewItem? SourceNavigationViewItem { get; }

    public object? Content { get; private set; }

    public string DisplayText => Content?.ToString() ?? string.Empty;

    public object? Icon { get; private set; }

    public InfoBadge? InfoBadge { get; private set; }

    public Type? TargetPageType { get; private set; }

    public string? TargetPageTag { get; private set; }

    public bool IsEnabled { get; private set; }

    public Visibility Visibility { get; private set; }

    public bool IsExpanded { get; private set; }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }

    public bool IsActiveBranch
    {
        get => _isActiveBranch;
        internal set => SetField(ref _isActiveBranch, value);
    }

    public int Depth { get; }

    public ModernNavigationEntry? Parent { get; }

    public List<ModernNavigationEntry> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public bool IsFooter { get; }

    public Visibility EffectiveVisibility =>
        Visibility == Visibility.Visible && (Parent is null || Parent.IsExpanded && Parent.EffectiveVisibility == Visibility.Visible)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? RebuildRequested;

    internal void AddChild(ModernNavigationEntry child)
    {
        Children.Add(child);
        OnPropertyChanged(nameof(HasChildren));
    }

    internal void SetExpanded(bool expanded)
    {
        SourceNavigationViewItem?.SetCurrentValue(NavigationViewItem.IsExpandedProperty, expanded);
        if (SourceNavigationViewItem is null)
        {
            IsExpanded = expanded;
            OnPropertyChanged(nameof(IsExpanded));
            NotifyDescendantVisibilityChanged();
        }
    }

    internal void NotifyDescendantVisibilityChanged()
    {
        foreach (var child in Children)
        {
            child.OnPropertyChanged(nameof(EffectiveVisibility));
            child.NotifyDescendantVisibilityChanged();
        }
    }

    private void RefreshFromSource()
    {
        if (SourceNavigationViewItem is { } item)
        {
            Content = item.Content;
            Icon = item.Icon;
            InfoBadge = item.InfoBadge;
            TargetPageType = item.TargetPageType;
            TargetPageTag = !string.IsNullOrWhiteSpace(item.TargetPageTag)
                ? item.TargetPageTag
                : item.Tag?.ToString() ?? item.Id;
            IsEnabled = item.IsEnabled;
            Visibility = item.Visibility;
            IsExpanded = item.IsExpanded;
        }
        else
        {
            Content = SourceItem;
            Icon = null;
            InfoBadge = null;
            TargetPageType = SourceItem as Type;
            TargetPageTag = (SourceItem as FrameworkElement)?.Tag?.ToString();
            IsEnabled = SourceItem is not UIElement element || element.IsEnabled;
            Visibility = SourceItem is UIElement visibleElement ? visibleElement.Visibility : Visibility.Visible;
        }

        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(InfoBadge));
        OnPropertyChanged(nameof(TargetPageType));
        OnPropertyChanged(nameof(TargetPageTag));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(Visibility));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(EffectiveVisibility));
        NotifyDescendantVisibilityChanged();
    }

    private void AttachSourceWatchers()
    {
        if (SourceNavigationViewItem is not { } item)
        {
            return;
        }

        AddWatcher(ContentDescriptor, item, OnSourceValueChanged);
        AddWatcher(IconDescriptor, item, OnSourceValueChanged);
        AddWatcher(BadgeDescriptor, item, OnSourceValueChanged);
        AddWatcher(VisibilityDescriptor, item, OnSourceValueChanged);
        AddWatcher(EnabledDescriptor, item, OnSourceValueChanged);
        AddWatcher(ExpandedDescriptor, item, OnExpandedChanged);
        AddWatcher(ItemsSourceDescriptor, item, OnItemsSourceChanged);
        if (item.MenuItems is INotifyCollectionChanged menuItems)
        {
            menuItems.CollectionChanged += OnChildrenCollectionChanged;
        }

        AttachItemsSourceCollection(item.MenuItemsSource);
    }

    private void OnSourceValueChanged(object? sender, EventArgs e) => RefreshFromSource();

    private void OnExpandedChanged(object? sender, EventArgs e)
    {
        RefreshFromSource();
        NotifyDescendantVisibilityChanged();
    }

    private void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        AttachItemsSourceCollection(SourceNavigationViewItem?.MenuItemsSource);
        RebuildRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildRequested?.Invoke(this, EventArgs.Empty);

    private void AttachItemsSourceCollection(object? source)
    {
        if (_itemsSourceCollection is not null)
        {
            _itemsSourceCollection.CollectionChanged -= OnChildrenCollectionChanged;
        }

        _itemsSourceCollection = source as INotifyCollectionChanged;
        if (_itemsSourceCollection is not null)
        {
            _itemsSourceCollection.CollectionChanged += OnChildrenCollectionChanged;
        }
    }

    public void Dispose()
    {
        foreach (var child in Children)
        {
            child.Dispose();
        }

        if (SourceNavigationViewItem is not { } item)
        {
            return;
        }

        RemoveWatcher(ContentDescriptor, item, OnSourceValueChanged);
        RemoveWatcher(IconDescriptor, item, OnSourceValueChanged);
        RemoveWatcher(BadgeDescriptor, item, OnSourceValueChanged);
        RemoveWatcher(VisibilityDescriptor, item, OnSourceValueChanged);
        RemoveWatcher(EnabledDescriptor, item, OnSourceValueChanged);
        RemoveWatcher(ExpandedDescriptor, item, OnExpandedChanged);
        RemoveWatcher(ItemsSourceDescriptor, item, OnItemsSourceChanged);
        if (item.MenuItems is INotifyCollectionChanged menuItems)
        {
            menuItems.CollectionChanged -= OnChildrenCollectionChanged;
        }

        AttachItemsSourceCollection(null);
    }

    private static void AddWatcher(DependencyPropertyDescriptor? descriptor, object component, EventHandler handler) =>
        descriptor?.AddValueChanged(component, handler);

    private static void RemoveWatcher(DependencyPropertyDescriptor? descriptor, object component, EventHandler handler) =>
        descriptor?.RemoveValueChanged(component, handler);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
