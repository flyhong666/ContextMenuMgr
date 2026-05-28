using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Represents the text Input Dialog.
/// </summary>
public static class TextInputDialog
{
    /// <summary>
    /// Shows async.
    /// </summary>
    public static Task<string?> ShowAsync(
        string title,
        string label,
        string initialText,
        double width = 500,
        double height = 200,
        bool multiline = false)
    {
        var window = new TextInputFluentWindow(title, label, initialText, width, height, multiline)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return Task.FromResult(window.ShowDialog() == true ? window.ResultText : null);
    }

    private sealed class TextInputFluentWindow : FluentWindow
    {
        private readonly System.Windows.Controls.TextBox _textBox;

        /// <summary>
        /// Executes text Input Fluent Window.
        /// </summary>
        public TextInputFluentWindow(string title, string label, string initialText, double width, double height, bool multiline)
        {
            Title = title;
            Width = width;
            Height = height;
            MinWidth = Math.Min(440, width);
            MinHeight = multiline ? 260 : 200;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            ResizeMode = multiline ? System.Windows.ResizeMode.CanResizeWithGrip : System.Windows.ResizeMode.NoResize;
            ExtendsContentIntoTitleBar = true;
            WindowBackdropType = WindowBackdropType.Mica;
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextFillColorPrimaryBrush"];
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ApplicationBackgroundBrush"];
            WindowChromeTitleBarFactory.Apply(this);

            var titleBar = WindowChromeTitleBarFactory.CreateCloseOnlyTitleBar(
                this,
                title,
                new SymbolIcon { Symbol = SymbolRegular.TextEditStyle24 });

            _textBox = new System.Windows.Controls.TextBox
            {
                Margin = new System.Windows.Thickness(16, 8, 16, 0),
                Text = initialText,
                AcceptsReturn = multiline,
                AcceptsTab = multiline,
                TextWrapping = System.Windows.TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = multiline
                    ? System.Windows.Controls.ScrollBarVisibility.Auto
                    : System.Windows.Controls.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = multiline
                    ? System.Windows.Controls.ScrollBarVisibility.Auto
                    : System.Windows.Controls.ScrollBarVisibility.Disabled,
                MinHeight = multiline ? 120 : 0,
                VerticalAlignment = multiline
                    ? System.Windows.VerticalAlignment.Stretch
                    : System.Windows.VerticalAlignment.Top
            };

            var okButton = new Button
            {
                 Content = "OK",
                MinWidth = 88,
                IsDefault = true,
                Margin = new System.Windows.Thickness(0, 0, 8, 0),
                Appearance = ControlAppearance.Primary,
                Icon = new SymbolIcon { Symbol = SymbolRegular.Checkmark24 }
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 88,
                IsCancel = true,
                Appearance = ControlAppearance.Secondary,
                Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 }
            };

            okButton.Click += (_, _) => DialogResult = true;

            Content = new System.Windows.Controls.Grid
            {
                RowDefinitions =
                {
                    new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto },
                    new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto },
                    new System.Windows.Controls.RowDefinition { Height = multiline ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : System.Windows.GridLength.Auto },
                    new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto }
                },
                Children =
                {
                    titleBar,
                    new System.Windows.Controls.TextBlock
                    {
                        Text = label,
                        FontSize = 13,
                        FontWeight = System.Windows.FontWeights.SemiBold,
                        Margin = new System.Windows.Thickness(16, 8, 16, 0)
                    },
                    _textBox,
                    new System.Windows.Controls.StackPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Horizontal,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        Margin = new System.Windows.Thickness(16, 20, 16, 16),
                        Children = { okButton, cancelButton }
                    }
                }
            };

            System.Windows.Controls.Grid.SetRow(titleBar, 0);
            System.Windows.Controls.Grid.SetRow((System.Windows.UIElement)((System.Windows.Controls.Grid)Content).Children[1], 1);
            System.Windows.Controls.Grid.SetRow(_textBox, 2);
            System.Windows.Controls.Grid.SetRow((System.Windows.UIElement)((System.Windows.Controls.Grid)Content).Children[3], 3);

            Loaded += (_, _) =>
            {
                _textBox.SelectAll();
                _textBox.Focus();
            };
        }

        public string ResultText => _textBox.Text;
    }
}
