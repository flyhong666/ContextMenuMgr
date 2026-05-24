using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ContextMenuMgr.Frontend.Behaviors;

public static class ClickOpenSubmenuBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ClickOpenSubmenuBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ClickOpenSubmenuState),
            typeof(ClickOpenSubmenuBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    private static ClickOpenSubmenuState GetState(MenuItem item)
    {
        if (item.GetValue(StateProperty) is ClickOpenSubmenuState state)
        {
            return state;
        }

        state = new ClickOpenSubmenuState();
        item.SetValue(StateProperty, state);
        return state;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem item)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            Detach(item);
        }

        if ((bool)e.NewValue)
        {
            Attach(item);
        }
    }

    private static void Attach(MenuItem item)
    {
        item.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        item.SubmenuOpened += OnSubmenuOpened;
        item.SubmenuClosed += OnSubmenuClosed;
        item.Unloaded += OnUnloaded;
    }

    private static void Detach(MenuItem item)
    {
        item.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        item.SubmenuOpened -= OnSubmenuOpened;
        item.SubmenuClosed -= OnSubmenuClosed;
        item.Unloaded -= OnUnloaded;
        item.ClearValue(StateProperty);
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MenuItem item || !item.HasItems)
        {
            return;
        }

        var state = GetState(item);
        e.Handled = true;

        CloseSiblingClickOpenSubmenus(item);

        state.OpeningFromClick = true;
        state.OpenedByClick = true;

        item.Focus();
        item.IsSubmenuOpen = true;

        item.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => state.OpeningFromClick = false));
    }

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        var state = GetState(item);
        if (state.OpeningFromClick || state.OpenedByClick)
        {
            return;
        }

        e.Handled = true;

        item.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                var currentState = GetState(item);
                if (!currentState.OpenedByClick)
                {
                    item.IsSubmenuOpen = false;
                }
            }));
    }

    private static void OnSubmenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        var state = GetState(item);
        state.OpeningFromClick = false;
        state.OpenedByClick = false;
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            Detach(item);
        }
    }

    private static void CloseSiblingClickOpenSubmenus(MenuItem current)
    {
        var parent = ItemsControl.ItemsControlFromItemContainer(current);
        if (parent is null)
        {
            return;
        }

        foreach (var sibling in GetSiblingMenuItems(parent))
        {
            if (ReferenceEquals(sibling, current) || !GetIsEnabled(sibling))
            {
                continue;
            }

            var state = GetState(sibling);
            state.OpenedByClick = false;
            state.OpeningFromClick = false;
            sibling.IsSubmenuOpen = false;
        }
    }

    private static IEnumerable<MenuItem> GetSiblingMenuItems(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (item is MenuItem menuItem)
            {
                yield return menuItem;
                continue;
            }

            if (parent.ItemContainerGenerator.ContainerFromItem(item) is MenuItem generatedMenuItem)
            {
                yield return generatedMenuItem;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is MenuItem menuItem)
            {
                yield return menuItem;
            }
        }
    }

    private sealed class ClickOpenSubmenuState
    {
        public bool OpeningFromClick { get; set; }
        public bool OpenedByClick { get; set; }
    }
}
