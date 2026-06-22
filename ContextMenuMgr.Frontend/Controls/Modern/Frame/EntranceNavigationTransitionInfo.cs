using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

public sealed class EntranceNavigationTransitionInfo : ModernNavigationTransitionInfo
{
    internal override Storyboard CreateEnterStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration)
    {
        var storyboard = new Storyboard();
        if (movingBackwards)
        {
            storyboard.Children.Add(Opacity(0, 1, duration, DecelerateKeySpline));
        }
        else
        {
            element.RenderTransform = new TranslateTransform();
            storyboard.Children.Add(Translate(TranslateYPath, 48, 0, duration, DecelerateKeySpline));
            storyboard.Children.Add(ImmediateOpacity(1));
        }

        return storyboard;
    }

    internal override Storyboard CreateExitStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration)
    {
        var storyboard = new Storyboard();
        if (movingBackwards)
        {
            element.RenderTransform = new TranslateTransform();
            storyboard.Children.Add(Translate(TranslateYPath, 0, 48, duration, AccelerateKeySpline));
            storyboard.Children.Add(Opacity(1, 0, duration, AccelerateKeySpline));
        }
        else
        {
            storyboard.Children.Add(Opacity(1, 0, TimeSpan.FromMilliseconds(Math.Min(150, duration.TotalMilliseconds)), AccelerateKeySpline));
        }

        return storyboard;
    }

    internal static DoubleAnimationUsingKeyFrames Translate(PropertyPath path, double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new DiscreteDoubleKeyFrame(from, TimeSpan.Zero), new SplineDoubleKeyFrame(to, duration, spline) }
        };
        Storyboard.SetTargetProperty(animation, path);
        return animation;
    }

    internal static DoubleAnimationUsingKeyFrames Opacity(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new DiscreteDoubleKeyFrame(from, TimeSpan.Zero), new SplineDoubleKeyFrame(to, duration, spline) }
        };
        Storyboard.SetTargetProperty(animation, OpacityPath);
        return animation;
    }

    internal static DoubleAnimation ImmediateOpacity(double value)
    {
        var animation = new DoubleAnimation(value, TimeSpan.Zero);
        Storyboard.SetTargetProperty(animation, OpacityPath);
        return animation;
    }
}
