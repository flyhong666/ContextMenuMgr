using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Services;

internal static class WindowChromeTitleBarFactory
{
    public const double CaptionHeight = 44;

    public static void Apply(FluentWindow window, double captionHeight = CaptionHeight)
    {
        window.WindowStyle = WindowStyle.None;
        window.ExtendsContentIntoTitleBar = true;

        void ApplyChrome()
        {
            var resizeBorderThickness = window.ResizeMode == ResizeMode.NoResize
                ? new Thickness(0)
                : new Thickness(6);

            var glassFrameThickness = window.WindowBackdropType == WindowBackdropType.None
                ? new Thickness(0.00001)
                : new Thickness(-1);

            SetChrome(window, captionHeight, resizeBorderThickness, glassFrameThickness);
        }

        window.SourceInitialized -= OnSourceInitialized;
        window.SourceInitialized += OnSourceInitialized;
        window.Dispatcher.BeginInvoke(ApplyChrome, DispatcherPriority.Loaded);

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            ApplyChrome();
        }
    }

    private static void SetChrome(
        Window window,
        double captionHeight,
        Thickness resizeBorderThickness,
        Thickness glassFrameThickness)
    {
        WindowChrome.SetWindowChrome(
            window,
            new WindowChrome
            {
                CaptionHeight = captionHeight,
                ResizeBorderThickness = resizeBorderThickness,
                GlassFrameThickness = glassFrameThickness,
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
    }

    public static Grid CreateCloseOnlyTitleBar(Window owner, string title, IconElement icon)
    {
        var titleBar = new Grid
        {
            Height = CaptionHeight,
            Background = Brushes.Transparent
        };

        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        icon.Width = 24;
        icon.Height = 24;
        icon.Margin = new Thickness(12, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        WindowChrome.SetIsHitTestVisibleInChrome(icon, true);
        Grid.SetColumn(icon, 0);
        titleBar.Children.Add(icon);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleText);

        var closeButton = new Wpf.Ui.Controls.Button
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 }
        };
        if (owner.TryFindResource("ChromeTitleBarButton") is Style buttonStyle)
        {
            closeButton.Style = buttonStyle;
        }

        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => SystemCommands.CloseWindow(owner);
        Grid.SetColumn(closeButton, 2);
        titleBar.Children.Add(closeButton);

        return titleBar;
    }
}
