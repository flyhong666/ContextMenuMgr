using System.Windows;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

public sealed class ModernFrameNavigatingEventArgs(
    FrameworkElement content,
    object? parameter,
    ModernFrameNavigationMode navigationMode,
    ModernNavigationTransitionInfo? transitionInfo)
    : ModernFrameNavigationEventArgs(content, parameter, navigationMode, transitionInfo)
{
    public bool Cancel { get; set; }
}
