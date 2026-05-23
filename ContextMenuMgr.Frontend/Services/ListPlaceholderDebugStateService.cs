using CommunityToolkit.Mvvm.ComponentModel;

namespace ContextMenuMgr.Frontend.Services;

public enum ListPlaceholderDebugMode
{
    None = 0,
    Loading = 1,
    Empty = 2
}

public sealed partial class ListPlaceholderDebugStateService : ObservableObject
{
    [ObservableProperty]
    public partial ListPlaceholderDebugMode Mode { get; set; }

    public bool ForceLoadingState => Mode == ListPlaceholderDebugMode.Loading;

    public bool ForceEmptyState => Mode == ListPlaceholderDebugMode.Empty;

    public bool HasForcedState => Mode != ListPlaceholderDebugMode.None;

    partial void OnModeChanged(ListPlaceholderDebugMode value)
    {
        OnPropertyChanged(nameof(ForceLoadingState));
        OnPropertyChanged(nameof(ForceEmptyState));
        OnPropertyChanged(nameof(HasForcedState));
    }

    public void SimulateLoading()
    {
        Mode = ListPlaceholderDebugMode.Loading;
    }

    public void SimulateEmpty()
    {
        Mode = ListPlaceholderDebugMode.Empty;
    }

    public void Clear()
    {
        Mode = ListPlaceholderDebugMode.None;
    }
}
