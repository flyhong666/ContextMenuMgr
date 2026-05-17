using CommunityToolkit.Mvvm.ComponentModel;

namespace ContextMenuMgr.Frontend.Services;

public sealed partial class ExplorerRestartStateService : ObservableObject
{
    private bool _needsRestart;

    public bool NeedsRestart
    {
        get => _needsRestart;
        set => SetProperty(ref _needsRestart, value);
    }

    public void MarkRequired()
    {
        NeedsRestart = true;
    }

    public void Clear()
    {
        NeedsRestart = false;
    }
}
