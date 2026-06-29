using System.Windows.Controls;
using ContextMenuMgr.Frontend.ViewModels;

namespace ContextMenuMgr.Frontend.Views;

/// <summary>
/// Represents the other Rules Page View.
/// </summary>
public partial class OtherRulesPageView : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OtherRulesPageView"/> class.
    /// </summary>
    public OtherRulesPageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not OtherRulesPageViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.EnsureSelectedTabLoadedAsync();
        }
        catch
        {
            // The owning tab ViewModel keeps failures local and user-readable.
        }
    }
}
