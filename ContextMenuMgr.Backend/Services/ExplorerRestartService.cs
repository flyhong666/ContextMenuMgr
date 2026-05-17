using System.Diagnostics;

namespace ContextMenuMgr.Backend.Services;

public sealed class ExplorerRestartService
{
    public void RestartExplorer(int? sessionId)
    {
        if (!sessionId.HasValue)
        {
            return;
        }

        var processes = Process.GetProcessesByName("explorer")
            .Where(p => p.SessionId == sessionId.Value)
            .ToArray();

        foreach (var process in processes)
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch
            {
            }
        }
    }
}
