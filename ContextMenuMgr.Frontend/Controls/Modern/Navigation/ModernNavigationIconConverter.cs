using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Controls.Modern.Navigation;

public sealed class ModernNavigationIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => CreateIcon(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    public static FrameworkElement CreateIcon(object? icon)
    {
        return icon switch
        {
            SymbolRegular symbol => CreateSymbolIcon(symbol),
            SymbolIcon symbolIcon => CreateSymbolIcon(symbolIcon.Symbol, symbolIcon.FontSize, symbolIcon.Filled),
            null => CreateFallbackIcon(),
            IconElement iconElement => Unsupported(iconElement),
            FrameworkElement element => Unsupported(element),
            _ => Unsupported(icon)
        };
    }

    private static SymbolIcon CreateSymbolIcon(SymbolRegular symbol, double fontSize = 20, bool filled = false) =>
        new(symbol, fontSize > 0 ? fontSize : 20, filled)
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static FrameworkElement Unsupported(object value)
    {
        Debug.WriteLine($"ModernNavigationView could not safely clone icon type '{value.GetType().FullName}'; using fallback.");
        return CreateFallbackIcon();
    }

    private static FrameworkElement CreateFallbackIcon() => CreateSymbolIcon(SymbolRegular.Document24);
}
