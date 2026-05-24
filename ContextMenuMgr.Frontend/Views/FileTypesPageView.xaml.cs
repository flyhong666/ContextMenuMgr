using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ContextMenuMgr.Frontend.Views;

/// <summary>
/// Represents the file Types Page View.
/// </summary>
public partial class FileTypesPageView : System.Windows.Controls.UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileTypesPageView"/> class.
    /// </summary>
    public FileTypesPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplySelectedTabIndex();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "SelectedTabIndex")
        {
            ApplySelectedTabIndex();
        }
    }

    private void ApplySelectedTabIndex()
    {
        if (DataContext is ViewModels.FileTypesPageViewModel vm && vm.SelectedTabIndex >= 0)
        {
            MainTabControl.SelectedIndex = vm.SelectedTabIndex;
        }
    }
}
