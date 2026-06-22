using System.Windows;
using System.Windows.Media.Animation;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

public abstract class ModernNavigationTransitionInfo : DependencyObject
{
    static ModernNavigationTransitionInfo()
    {
        AccelerateKeySpline = new KeySpline(0.7, 0, 1, 0.5);
        AccelerateKeySpline.Freeze();
        DecelerateKeySpline = new KeySpline(0.1, 0.9, 0.2, 1);
        DecelerateKeySpline.Freeze();
    }

    internal static readonly KeySpline AccelerateKeySpline;
    internal static readonly KeySpline DecelerateKeySpline;
    internal static readonly PropertyPath OpacityPath = new(UIElement.OpacityProperty);
    internal static readonly PropertyPath TranslateXPath = new("(UIElement.RenderTransform).(TranslateTransform.X)");
    internal static readonly PropertyPath TranslateYPath = new("(UIElement.RenderTransform).(TranslateTransform.Y)");

    internal virtual Storyboard? CreateEnterStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration) => null;
    internal virtual Storyboard? CreateExitStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration) => null;
}
