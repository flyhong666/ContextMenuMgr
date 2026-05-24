using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ContextMenuMgr.Frontend.Views;

/// <summary>
/// Represents the other Rules Page View.
/// </summary>
public partial class OtherRulesPageView : System.Windows.Controls.Page
{
    // WPF-UI's NavigationView wraps page content in a DynamicScrollViewer that gives
    // content infinite height, causing split-panel tabs to scroll together instead of
    // independently. We disable this outer ScrollViewer for split-panel tabs so that
    // the content height is constrained to the viewport, allowing inner ScrollViewers
    // (ListView built-in and the one wrapping ItemsControl) to work independently.
    private ScrollViewer? _outerScrollViewer;
    private ScrollBarVisibility _originalVerticalScrollBarVisibility;
    private bool _isOuterScrollViewerDisabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="OtherRulesPageView"/> class.
    /// </summary>
    public OtherRulesPageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MainTabControl.SelectionChanged += OnTabSelectionChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Find the outer DynamicScrollViewer injected by WPF-UI NavigationView
        _outerScrollViewer = FindParentScrollViewer(this);
        if (_outerScrollViewer is null) return;

        _originalVerticalScrollBarVisibility = _outerScrollViewer.VerticalScrollBarVisibility;
        UpdateOuterScrollViewer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Restore the outer ScrollViewer when navigating away so other pages are unaffected
        RestoreOuterScrollViewer();
        _outerScrollViewer = null;
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOuterScrollViewer();
    }

    /// <summary>
    /// Toggles the outer ScrollViewer based on the currently selected tab.
    /// Split-panel tabs (EnhanceMenus=0, DetailedEdit=1) need it disabled
    /// so left/right panels scroll independently. Other tabs need it enabled.
    /// </summary>
    private void UpdateOuterScrollViewer()
    {
        if (_outerScrollViewer is null) return;

        // var selectedIndex = MainTabControl.SelectedIndex;
        // var needsDisable = selectedIndex is 0 or 1;
        var selectedItem = (TabItem)MainTabControl.SelectedItem;
        var needsDisable = selectedItem.Tag is "EnhanceMenus" or "DetailedEdit";

        // Only modify the property when the state actually changes to avoid layout thrashing
        if (needsDisable && !_isOuterScrollViewerDisabled)
        {
            _outerScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _isOuterScrollViewerDisabled = true;
        }
        else if (!needsDisable && _isOuterScrollViewerDisabled)
        {
            _outerScrollViewer.VerticalScrollBarVisibility = _originalVerticalScrollBarVisibility;
            _isOuterScrollViewerDisabled = false;
        }
    }

    private void RestoreOuterScrollViewer()
    {
        if (_outerScrollViewer is null || !_isOuterScrollViewerDisabled) return;

        _outerScrollViewer.VerticalScrollBarVisibility = _originalVerticalScrollBarVisibility;
        _isOuterScrollViewerDisabled = false;
    }

    /// <summary>
    /// Walks up the visual tree to find the first parent ScrollViewer.
    /// </summary>
    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is ScrollViewer sv) return sv;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
