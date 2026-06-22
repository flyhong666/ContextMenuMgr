using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ContextMenuMgr.Frontend.Services;
using ContextMenuMgr.Frontend.ViewModels;

namespace ContextMenuMgr.Frontend.Views;

public partial class ApplicationGroupsPageView : Page, INavigationScrollTarget
{
    public ApplicationGroupsPageView() => InitializeComponent();

    public void ApplyNavigationScrollPosition(ScrollViewer navigationScrollViewer)
    {
        if (DataContext is not ApplicationGroupsPageViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.ScrollTargetItemId))
        {
            return;
        }

        UpdateLayout();
        navigationScrollViewer.UpdateLayout();

        var itemId = viewModel.ScrollTargetItemId;
        var target = FindTaggedBorder(this, itemId);
        if (target is null)
        {
            FrontendDebugLog.Warning(
                nameof(ApplicationGroupsPageView),
                $"Application group navigation target was not found. ItemId={itemId}.");
            return;
        }

        var targetPosition = target
            .TransformToAncestor(navigationScrollViewer)
            .Transform(new Point(0, 0));
        var desiredOffset = Math.Clamp(
            navigationScrollViewer.VerticalOffset + targetPosition.Y,
            0,
            navigationScrollViewer.ScrollableHeight);
        navigationScrollViewer.ScrollToVerticalOffset(desiredOffset);
        viewModel.AcknowledgeScrollTarget(itemId);
    }

    private static Border? FindTaggedBorder(DependencyObject parent, string itemId)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Border { Tag: string tag } border
                && string.Equals(tag, itemId, StringComparison.OrdinalIgnoreCase))
            {
                return border;
            }

            var match = FindTaggedBorder(child, itemId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
