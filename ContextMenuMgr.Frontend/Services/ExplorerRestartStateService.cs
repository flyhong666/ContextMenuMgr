using CommunityToolkit.Mvvm.ComponentModel;

namespace ContextMenuMgr.Frontend.Services;

public sealed partial class ExplorerRestartStateService : ObservableObject
{
    private bool _needsRestart;
    private bool _needsIconCacheRefresh;

    public bool NeedsRestart
    {
        get => _needsRestart;
        set => SetProperty(ref _needsRestart, value);
    }

    public bool NeedsIconCacheRefresh
    {
        get => _needsIconCacheRefresh;
        set => SetProperty(ref _needsIconCacheRefresh, value);
    }

    public void MarkRequired()
    {
        NeedsRestart = true;
    }

    public void Clear()
    {
        NeedsRestart = false;
    }

    public void MarkIconCacheRefreshRequired()
    {
        NeedsIconCacheRefresh = true;
    }

    public void ClearIconCacheRefresh()
    {
        NeedsIconCacheRefresh = false;
    }
}
