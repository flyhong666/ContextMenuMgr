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
            SymbolIcon symbolIcon => CopyCommonProperties(
                symbolIcon,
                CreateSymbolIcon(symbolIcon.Symbol, symbolIcon.FontSize, symbolIcon.Filled)),
            ImageIcon imageIcon => CopyCommonProperties(imageIcon, new ImageIcon { Source = imageIcon.Source }),
            IconSourceElement sourceElement => CreateFromIconSource(sourceElement),
            FontIcon fontIcon => CopyCommonProperties(fontIcon, new FontIcon
            {
                FontFamily = fontIcon.FontFamily,
                FontSize = fontIcon.FontSize,
                FontStyle = fontIcon.FontStyle,
                FontWeight = fontIcon.FontWeight,
                Glyph = fontIcon.Glyph
            }),
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

    private static FrameworkElement CreateFromIconSource(IconSourceElement sourceElement)
    {
        try
        {
            return sourceElement.CreateIconElement() is { } icon
                ? CopyCommonProperties(sourceElement, icon)
                : Unsupported(sourceElement);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"ModernNavigationView could not create icon from '{sourceElement.GetType().FullName}': {exception.Message}");
            return CreateFallbackIcon();
        }
    }

    private static T CopyCommonProperties<T>(IconElement source, T target) where T : IconElement
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.MinWidth = source.MinWidth;
        target.MinHeight = source.MinHeight;
        target.MaxWidth = source.MaxWidth;
        target.MaxHeight = source.MaxHeight;
        target.Margin = source.Margin;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;

        if (BindingOperations.GetBindingBase(source, IconElement.ForegroundProperty) is { } foregroundBinding)
        {
            BindingOperations.SetBinding(target, IconElement.ForegroundProperty, foregroundBinding);
        }
        else if (source.ReadLocalValue(IconElement.ForegroundProperty) != DependencyProperty.UnsetValue)
        {
            target.SetCurrentValue(IconElement.ForegroundProperty, source.Foreground);
        }

        return target;
    }

    private static FrameworkElement Unsupported(object value)
    {
        Debug.WriteLine($"ModernNavigationView could not safely clone icon type '{value.GetType().FullName}'; using fallback.");
        return CreateFallbackIcon();
    }

    private static FrameworkElement CreateFallbackIcon() => CreateSymbolIcon(SymbolRegular.Document24);
}
