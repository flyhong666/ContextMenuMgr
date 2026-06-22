using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ContextMenuMgr.Frontend.Controls.Modern.Scrolling;

internal static class WheelScrollEventGuard
{
    public static bool ShouldSkipSmoothScroll(
        ModernScrollViewer owner,
        MouseWheelEventArgs e,
        DependencyObject? source)
    {
        if (e.Handled
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return true;
        }

        foreach (var current in EnumerateAncestors(source))
        {
            if (ReferenceEquals(current, owner))
            {
                return false;
            }

            if (current is Popup or ContextMenu
                || current.GetType().Name.Contains("PopupRoot", StringComparison.Ordinal)
                || current is ComboBox { IsDropDownOpen: true })
            {
                return true;
            }

            var ownership = ModernScroll.GetOwnership(current);
            if (ownership == ModernScrollOwnership.Self)
            {
                return true;
            }

            if (ownership == ModernScrollOwnership.Frame)
            {
                return false;
            }

            if (current is ScrollViewer nested && CanNestedViewerScroll(nested, e.Delta))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanNestedViewerScroll(ScrollViewer viewer, int delta)
    {
        if (viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return delta switch
        {
            < 0 => viewer.VerticalOffset < viewer.ScrollableHeight,
            > 0 => viewer.VerticalOffset > 0,
            _ => false
        };
    }

    private static IEnumerable<DependencyObject> EnumerateAncestors(DependencyObject? source)
    {
        var current = source;
        var visited = new HashSet<DependencyObject>();
        while (current is not null && visited.Add(current))
        {
            yield return current;
            current = GetParent(current);
        }
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Popup popup)
        {
            return popup.PlacementTarget;
        }

        if (current is ContextMenu contextMenu)
        {
            return contextMenu.PlacementTarget;
        }

        if (current is Visual or Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return current switch
        {
            FrameworkElement element => element.Parent ?? element.TemplatedParent,
            FrameworkContentElement contentElement => contentElement.Parent ?? contentElement.TemplatedParent,
            _ => LogicalTreeHelper.GetParent(current)
        };
    }
}
