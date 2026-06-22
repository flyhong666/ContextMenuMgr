using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace ContextMenuMgr.Frontend.Controls.Modern.Navigation;

/// <summary>
/// A compact NavigationView-like selector for in-page grouping. It only selects items
/// and does not create frames or run navigation.
/// </summary>
public partial class ModernNavigationSelector : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(ModernNavigationSelector),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(ModernNavigationSelector),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedItemChanged));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(ModernNavigationSelector),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(object),
        typeof(ModernNavigationSelector),
        new PropertyMetadata(null, OnHeaderChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(object),
        typeof(ModernNavigationSelector),
        new PropertyMetadata(null, OnHeaderChanged));

    public static readonly DependencyProperty IsSelectionRequiredProperty = DependencyProperty.Register(
        nameof(IsSelectionRequired),
        typeof(bool),
        typeof(ModernNavigationSelector),
        new PropertyMetadata(true, OnIsSelectionRequiredChanged));

    private bool _coercingSelection;

    public ModernNavigationSelector()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateHeaderVisibility();
            EnsureRequiredSelection();
        };
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsSelectionRequired
    {
        get => (bool)GetValue(IsSelectionRequiredProperty);
        set => SetValue(IsSelectionRequiredProperty, value);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_coercingSelection)
        {
            return;
        }

        if (PART_ListBox.SelectedItem is null && IsSelectionRequired)
        {
            EnsureRequiredSelection();
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ModernNavigationSelector)d).EnsureRequiredSelection();

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (ModernNavigationSelector)d;
        if (selector.IsSelectionRequired && e.NewValue is null)
        {
            selector.EnsureRequiredSelection();
        }
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ModernNavigationSelector)d).UpdateHeaderVisibility();

    private static void OnIsSelectionRequiredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ModernNavigationSelector)d).EnsureRequiredSelection();

    private void EnsureRequiredSelection()
    {
        if (!IsSelectionRequired || ItemsSource is null || SelectedItem is not null)
        {
            return;
        }

        var firstItem = ItemsSource.Cast<object?>().FirstOrDefault();
        if (firstItem is null)
        {
            return;
        }

        _coercingSelection = true;
        try
        {
            SetCurrentValue(SelectedItemProperty, firstItem);
        }
        finally
        {
            _coercingSelection = false;
        }
    }

    private void UpdateHeaderVisibility()
    {
        HeaderPresenter.Visibility = Header is null ? Visibility.Collapsed : Visibility.Visible;
        DescriptionPresenter.Visibility = Description is null ? Visibility.Collapsed : Visibility.Visible;
        HeaderPanel.Visibility = Header is null && Description is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
