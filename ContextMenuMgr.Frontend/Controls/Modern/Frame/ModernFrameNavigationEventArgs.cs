using System.Windows;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

public class ModernFrameNavigationEventArgs(
    FrameworkElement content,
    object? parameter,
    ModernFrameNavigationMode navigationMode,
    ModernNavigationTransitionInfo? transitionInfo) : EventArgs
{
    public FrameworkElement Content { get; } = content;
    public object? Parameter { get; } = parameter;
    public ModernFrameNavigationMode NavigationMode { get; } = navigationMode;
    public ModernNavigationTransitionInfo? TransitionInfo { get; } = transitionInfo;
}
