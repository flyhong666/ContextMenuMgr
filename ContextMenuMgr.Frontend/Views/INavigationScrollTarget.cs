using System.Windows.Controls;

namespace ContextMenuMgr.Frontend.Views;

/// <summary>
/// Applies a page-specific position after the shared navigation scroll viewer
/// has been reset for newly navigated content.
/// </summary>
public interface INavigationScrollTarget
{
    void ApplyNavigationScrollPosition(ScrollViewer navigationScrollViewer);
}
