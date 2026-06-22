using System.Windows;
using System.Windows.Media.Animation;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

public sealed class FadeNavigationTransitionInfo : ModernNavigationTransitionInfo
{
    internal override Storyboard CreateEnterStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(EntranceNavigationTransitionInfo.Opacity(0, 1, duration, DecelerateKeySpline));
        return storyboard;
    }

    internal override Storyboard CreateExitStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(EntranceNavigationTransitionInfo.Opacity(
            1, 0, TimeSpan.FromMilliseconds(Math.Min(150, duration.TotalMilliseconds)), AccelerateKeySpline));
        return storyboard;
    }
}
