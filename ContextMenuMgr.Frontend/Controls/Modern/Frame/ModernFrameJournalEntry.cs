using System.Windows;

namespace ContextMenuMgr.Frontend.Controls.Modern.Frame;

internal sealed record ModernFrameJournalEntry(
    FrameworkElement Content,
    object? Parameter,
    ModernNavigationTransitionInfo? TransitionInfo);
