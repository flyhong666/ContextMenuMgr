using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

public sealed class SlideNavigationTransitionInfo : ModernNavigationTransitionInfo
{
    public static readonly DependencyProperty EffectProperty = DependencyProperty.Register(
        nameof(Effect), typeof(SlideNavigationTransitionEffect), typeof(SlideNavigationTransitionInfo),
        new PropertyMetadata(SlideNavigationTransitionEffect.FromBottom));

    public SlideNavigationTransitionEffect Effect
    {
        get => (SlideNavigationTransitionEffect)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    internal override Storyboard CreateEnterStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration)
    {
        element.RenderTransform = new TranslateTransform();
        var storyboard = new Storyboard();
        if (Effect == SlideNavigationTransitionEffect.FromBottom)
        {
            storyboard.Children.Add(movingBackwards
                ? EntranceNavigationTransitionInfo.Opacity(0, 1, duration, DecelerateKeySpline)
                : EntranceNavigationTransitionInfo.Translate(TranslateYPath, 48, 0, duration, DecelerateKeySpline));
        }
        else
        {
            var fromLeft = Effect == SlideNavigationTransitionEffect.FromLeft ? !movingBackwards : movingBackwards;
            storyboard.Children.Add(EntranceNavigationTransitionInfo.Translate(
                TranslateXPath, fromLeft ? -96 : 96, 0, duration, DecelerateKeySpline));
        }

        storyboard.Children.Add(EntranceNavigationTransitionInfo.ImmediateOpacity(1));
        return storyboard;
    }

    internal override Storyboard CreateExitStoryboard(FrameworkElement element, bool movingBackwards, TimeSpan duration)
    {
        element.RenderTransform = new TranslateTransform();
        var storyboard = new Storyboard();
        if (Effect == SlideNavigationTransitionEffect.FromBottom)
        {
            if (movingBackwards)
            {
                storyboard.Children.Add(EntranceNavigationTransitionInfo.Translate(
                    TranslateYPath, 0, 48, duration, AccelerateKeySpline));
            }
        }
        else
        {
            var toLeft = Effect == SlideNavigationTransitionEffect.FromLeft ? movingBackwards : !movingBackwards;
            storyboard.Children.Add(EntranceNavigationTransitionInfo.Translate(
                TranslateXPath, 0, toLeft ? -96 : 96, duration, AccelerateKeySpline));
        }

        storyboard.Children.Add(EntranceNavigationTransitionInfo.Opacity(
            1, 0, TimeSpan.FromMilliseconds(Math.Min(150, duration.TotalMilliseconds)), AccelerateKeySpline));
        return storyboard;
    }
}
