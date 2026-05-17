using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ContextMenuMgr.Frontend.Services;
using ContextMenuMgr.Frontend.Views.Pages;
using Wpf.Ui.Appearance;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend;

/// <summary>
/// Represents the main Window.
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly UpdateCheckService _updateCheckService;
    private Type? _pendingPageType;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow(
        ViewModels.ShellViewModel viewModel,
        IServiceProvider serviceProvider,
        INavigationService navigationService,
        IInfoBarService infoBarService,
        UpdateCheckService updateCheckService)
    {
        _updateCheckService = updateCheckService;
        SystemThemeWatcher.Watch(this);
        InitializeComponent();
        infoBarService.SetInfoBarControl(RootInfoBar, RootInfoBarTitle, RootInfoBarMessage, RootInfoBarLink);
        RootInfoBarClose.Click += (_, _) => infoBarService.CloseInfoBar();
        RootNavigation.SetServiceProvider(serviceProvider);
        navigationService.SetNavigationControl(RootNavigation);
        RootNavigation.Navigated += OnRootNavigationNavigated;
        DataContext = viewModel;

        ApplyWindowIcon();
#if DEBUG
        AddDebugUpdatePromptButton();
#endif
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
        _updateCheckService.StartInitialCheck();
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

#if DEBUG
    private void AddDebugUpdatePromptButton()
    {
        var button = new Wpf.Ui.Controls.Button
        {
            MinWidth = 108,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Appearance = ControlAppearance.Secondary,
            Content = "强制更新提示"
        };
        button.Click += (_, _) => _updateCheckService.ShowDebugUpdatePrompt();
        HeaderActions.Children.Insert(0, button);
    }
#endif
}
