using ContextMenuMgr.Frontend.ViewModels;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Views;

public partial class ContextMenuDeepAnalysisWindow : FluentWindow
{
    public ContextMenuDeepAnalysisWindow(ContextMenuDeepAnalysisWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SetCloseAction(Close);
    }
}
