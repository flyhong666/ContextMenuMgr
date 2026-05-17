using Wpf.Ui.Controls;
using WpfBorder = System.Windows.Controls.Border;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace ContextMenuMgr.Frontend.Services;

public interface IInfoBarService
{
    void SetInfoBarControl(WpfBorder host, WpfTextBlock titleBlock, WpfTextBlock messageBlock, HyperlinkButton linkButton);

    void ShowInformationalInfoBar(string title, string message);

    void ShowInformationalInfoBar(string title, string message, string linkText, string linkUrl);

    void CloseInfoBar();
}
