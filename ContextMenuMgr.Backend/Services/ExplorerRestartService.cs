using System.Diagnostics;

namespace ContextMenuMgr.Backend.Services;

public sealed class ExplorerRestartService
{
    public int RestartExplorer(int? sessionId)
    {
        if (!sessionId.HasValue)
        {
            return 0;
        }

        var killed = 0;
        var processes = Process.GetProcessesByName("explorer");

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId.Value)
                    {
                        continue;
                    }

                    process.Kill();
                    process.WaitForExit(5000);
                    killed++;
                }
                catch
                {
                }
            }
        }

        return killed;
    }
}
