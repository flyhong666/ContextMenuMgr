using System.Windows;
using Wpf.Ui.Controls;
using WpfBorder = System.Windows.Controls.Border;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace ContextMenuMgr.Frontend.Services;

internal sealed class InfoBarService : IInfoBarService
{
    private WpfBorder? _host;
    private WpfTextBlock? _titleBlock;
    private WpfTextBlock? _messageBlock;
    private HyperlinkButton? _linkButton;

    public void SetInfoBarControl(WpfBorder host, WpfTextBlock titleBlock, WpfTextBlock messageBlock, HyperlinkButton linkButton)
    {
        _host = host;
        _titleBlock = titleBlock;
        _messageBlock = messageBlock;
        _linkButton = linkButton;
    }

    public void ShowInformationalInfoBar(string title, string message)
    {
        Show(title, message, linkText: null, linkUrl: null);
    }

    public void ShowInformationalInfoBar(string title, string message, string linkText, string linkUrl)
    {
        Show(title, message, linkText, linkUrl);
    }

    public void CloseInfoBar()
    {
        if (_host is null)
        {
            return;
        }

        void Close() => _host.Visibility = Visibility.Collapsed;

        if (_host.Dispatcher.CheckAccess())
        {
            Close();
            return;
        }

        _ = _host.Dispatcher.BeginInvoke((Action)Close);
    }

    private void Show(string title, string message, string? linkText, string? linkUrl)
    {
        if (_host is null || _titleBlock is null || _messageBlock is null || _linkButton is null)
        {
            return;
        }

        void ShowCore()
        {
            _titleBlock.Text = title;
            _messageBlock.Text = message;

            if (string.IsNullOrWhiteSpace(linkText) || string.IsNullOrWhiteSpace(linkUrl))
            {
                _linkButton.Content = null;
                _linkButton.NavigateUri = string.Empty;
                _linkButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                _linkButton.Content = linkText;
                _linkButton.NavigateUri = linkUrl;
                _linkButton.Visibility = Visibility.Visible;
            }

            _host.Visibility = Visibility.Visible;
        }

        if (_host.Dispatcher.CheckAccess())
        {
            ShowCore();
            return;
        }

        _ = _host.Dispatcher.BeginInvoke((Action)ShowCore);
    }
}
