using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ContextMenuMgr.Frontend.Controls.Modern.Scrolling;

/// <summary>
/// The navigation frame's single outer scrolling surface. Wheel input is animated
/// only when no nested scroll owner can consume it.
/// </summary>
public sealed class ModernScrollViewer : ScrollViewer
{
    public static readonly DependencyProperty IsSmoothScrollingEnabledProperty = DependencyProperty.Register(
        nameof(IsSmoothScrollingEnabled), typeof(bool), typeof(ModernScrollViewer), new PropertyMetadata(true));

    public static readonly DependencyProperty WheelScrollMultiplierProperty = DependencyProperty.Register(
        nameof(WheelScrollMultiplier), typeof(double), typeof(ModernScrollViewer), new PropertyMetadata(1D));

    public static readonly DependencyProperty ScrollAnimationDurationProperty = DependencyProperty.Register(
        nameof(ScrollAnimationDuration), typeof(int), typeof(ModernScrollViewer),
        new PropertyMetadata((int)ScrollAnimationHelper.DefaultDuration.TotalMilliseconds));

    public static readonly DependencyProperty ScrollEasingFunctionProperty = DependencyProperty.Register(
        nameof(ScrollEasingFunction), typeof(IEasingFunction), typeof(ModernScrollViewer), new PropertyMetadata(null));

    public ModernScrollViewer()
    {
        PreviewMouseWheel += OnPreviewMouseWheel;
        Unloaded += (_, _) => ScrollAnimationHelper.CancelVerticalAnimation(this);
    }

    public bool IsSmoothScrollingEnabled
    {
        get => (bool)GetValue(IsSmoothScrollingEnabledProperty);
        set => SetValue(IsSmoothScrollingEnabledProperty, value);
    }

    public double WheelScrollMultiplier
    {
        get => (double)GetValue(WheelScrollMultiplierProperty);
        set => SetValue(WheelScrollMultiplierProperty, value);
    }

    public int ScrollAnimationDuration
    {
        get => (int)GetValue(ScrollAnimationDurationProperty);
        set => SetValue(ScrollAnimationDurationProperty, value);
    }

    public IEasingFunction? ScrollEasingFunction
    {
        get => (IEasingFunction?)GetValue(ScrollEasingFunctionProperty);
        set => SetValue(ScrollEasingFunctionProperty, value);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsSmoothScrollingEnabled
            || WheelScrollEventGuard.ShouldSkipSmoothScroll(this, e, e.OriginalSource as DependencyObject))
        {
            return;
        }

        ScrollVerticalWheel(e);
    }

    private void ScrollVerticalWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || ScrollableHeight <= 0 || !CanScrollInDirection(e.Delta))
        {
            return;
        }

        var notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        var wheelLines = Math.Max(1, SystemParameters.WheelScrollLines);
        var change = notches * wheelLines * 16 * Math.Max(0.1, WheelScrollMultiplier);
        var currentTarget = ScrollAnimationHelper.GetCurrentVerticalAnimationTarget(this) ?? VerticalOffset;

        ScrollAnimationHelper.SmoothScrollToVerticalOffset(
            this,
            currentTarget - change,
            TimeSpan.FromMilliseconds(Math.Max(0, ScrollAnimationDuration)),
            ScrollAnimationDuration > 0,
            ScrollEasingFunction);
        e.Handled = true;
    }

    internal bool CanScrollInDirection(int wheelDelta)
    {
        var offset = ScrollAnimationHelper.GetCurrentVerticalAnimationTarget(this) ?? VerticalOffset;
        return wheelDelta switch
        {
            < 0 => offset < ScrollableHeight,
            > 0 => offset > 0,
            _ => false
        };
    }
}
