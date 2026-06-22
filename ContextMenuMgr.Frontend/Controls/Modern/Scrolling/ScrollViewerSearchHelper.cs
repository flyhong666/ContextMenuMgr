using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ContextMenuMgr.Frontend.Controls.Modern.Scrolling;

public static class ScrollViewerSearchHelper
{
    public static ScrollViewer? FindNearestAncestor(DependencyObject? target)
    {
        var current = target;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
