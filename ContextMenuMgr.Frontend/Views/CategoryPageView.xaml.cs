using System.Windows;
using System.Windows.Controls;
using ContextMenuMgr.Frontend.Services;
using ContextMenuMgr.Frontend.ViewModels;
using ContextMenuMgr.Frontend.Views.Pages;
using Wpf.Ui;

namespace ContextMenuMgr.Frontend.Views;

/// <summary>
/// Represents the category Page View.
/// </summary>
public partial class CategoryPageView : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CategoryPageView"/> class.
    /// </summary>
    public CategoryPageView()
    {
        InitializeComponent();
    }

    private void OpenApplicationGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ContextMenuItemViewModel item }
            || Application.Current is not App app
            || app.TryGetService<GlobalSearchNavigationFilterService>() is not { } filterService
            || app.TryGetService<INavigationService>() is not { } navigationService)
        {
            return;
        }

        filterService.RequestFilter(
            typeof(ApplicationGroupsPage),
            item.Category,
            isWindows11: false,
            filterText: item.DisplayName,
            item.Id);
        navigationService.Navigate(typeof(ApplicationGroupsPage));
    }
}
