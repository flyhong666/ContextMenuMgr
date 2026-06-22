using System.Collections;
using System.Windows;
using Wpf.Ui.Controls;

namespace ContextMenuMgr.Frontend.Controls.Modern.Navigation;

internal static class ModernNavigationEntryCollectionBuilder
{
    public static IEnumerable<ModernNavigationEntry> Build(
        IEnumerable source,
        bool isFooter,
        object? inheritedDataContext,
        EventHandler rebuildRequested)
    {
        foreach (var item in source.Cast<object>())
        {
            foreach (var entry in BuildItem(item, null, 0, isFooter, inheritedDataContext, rebuildRequested))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<ModernNavigationEntry> BuildItem(
        object item,
        ModernNavigationEntry? parent,
        int depth,
        bool isFooter,
        object? inheritedDataContext,
        EventHandler rebuildRequested)
    {
        if (item is FrameworkElement element && element.ReadLocalValue(FrameworkElement.DataContextProperty) == DependencyProperty.UnsetValue)
        {
            element.SetCurrentValue(FrameworkElement.DataContextProperty, inheritedDataContext);
        }

        var entry = new ModernNavigationEntry(item, parent, depth, isFooter);
        entry.RebuildRequested += rebuildRequested;
        parent?.AddChild(entry);
        yield return entry;

        if (item is not NavigationViewItem navigationItem)
        {
            yield break;
        }

        foreach (var child in EnumerateChildren(navigationItem))
        {
            foreach (var descendant in BuildItem(child, entry, depth + 1, isFooter, inheritedDataContext, rebuildRequested))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<object> EnumerateChildren(NavigationViewItem item)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (item.MenuItems is IEnumerable menuItems)
        {
            foreach (var child in menuItems.Cast<object>())
            {
                if (seen.Add(child))
                {
                    yield return child;
                }
            }
        }

        if (item.MenuItemsSource is IEnumerable source and not string)
        {
            foreach (var child in source.Cast<object>())
            {
                if (seen.Add(child))
                {
                    yield return child;
                }
            }
        }
    }
}
