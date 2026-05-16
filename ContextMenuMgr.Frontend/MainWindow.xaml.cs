using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ContextMenuMgr.Frontend.Views.Pages;
using Wpf.Ui.Appearance;

namespace ContextMenuMgr.Frontend;

/// <summary>
/// Represents the main Window.
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private Type? _pendingPageType;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow(
        ViewModels.ShellViewModel viewModel,
        IServiceProvider serviceProvider)
    {
        SystemThemeWatcher.Watch(this);
        InitializeComponent();
        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Navigated += OnRootNavigationNavigated;
        DataContext = viewModel;

        ApplyWindowIcon();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Navigates to to.
    /// </summary>
    public void NavigateTo(Type pageType)
    {
        _pendingPageType = pageType;
        if (IsLoaded)
        {
            RootNavigation.Navigate(pageType);
        }
    }

    /// <summary>
    /// Executes bring To Foreground.
    /// </summary>
    public void BringToForeground()
    {
        var previousTopmost = Topmost;
        Topmost = true;
        Activate();
        Topmost = previousTopmost;
        Focus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var targetPageType = _pendingPageType ?? typeof(FileContextMenuPage);
        RootNavigation.Navigate(targetPageType);
    }

    private void OnRootNavigationNavigated(
        Wpf.Ui.Controls.NavigationView sender,
        Wpf.Ui.Controls.NavigatedEventArgs args)
    {
        QueueNavigationScrollReset();
    }

    private void QueueNavigationScrollReset()
    {
        _ = Dispatcher.BeginInvoke(
            new Action(ResetNavigationFrameScrollOffset),
            DispatcherPriority.Loaded);
    }

    private void ResetNavigationFrameScrollOffset()
    {
        var frame = FindDescendant<Wpf.Ui.Controls.NavigationViewContentPresenter>(RootNavigation);
        if (frame is null)
        {
            return;
        }

        // WPF-UI wraps NavigationViewContentPresenter in its own DynamicScrollViewer.
        // That viewer is shared by all navigated pages, so its offset must be reset
        // whenever the NavigationView switches content. Avoid touching page-owned
        // ListBox/ScrollViewer instances so each page keeps control of its own lists.
        var navigationScrollViewer = FindDescendant<ScrollViewer>(
            frame,
            scrollViewer => ReferenceEquals(scrollViewer.TemplatedParent, frame));

        navigationScrollViewer?.ScrollToHome();
        navigationScrollViewer?.ScrollToLeftEnd();
    }

    private static T? FindDescendant<T>(DependencyObject root, Predicate<T>? predicate = null)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild && (predicate is null || predicate(typedChild)))
            {
                return typedChild;
            }

            var descendant = FindDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }
    }
}
