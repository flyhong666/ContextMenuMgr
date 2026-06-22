using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ContextMenuMgr.Frontend.Controls.Modern.Navigation;

public sealed class NavigationDepthToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? depth * 22 : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
