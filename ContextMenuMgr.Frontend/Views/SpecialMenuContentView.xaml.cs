using System.Windows.Controls;
using ContextMenuMgr.Frontend.ViewModels;

namespace ContextMenuMgr.Frontend.Views;

/// <summary>
/// Represents the reusable special menu content view.
/// </summary>
public partial class SpecialMenuContentView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialMenuContentView"/> class.
    /// </summary>
    public SpecialMenuContentView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not SpecialMenuPageViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

}
