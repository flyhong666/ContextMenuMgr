using System.IO;

namespace ContextMenuMgr.Frontend.Services;

public sealed record RuntimeDataAclRepairClientResult(
    bool Success,
    bool Cancelled,
    string Code,
    string Detail);

public sealed class RuntimeDataAclRepairClient
{
    private static readonly TimeSpan CancelledPromptSuppression = TimeSpan.FromMinutes(5);
    private readonly IBackendClient _backendClient;
    private readonly IBackendServiceManager _backendServiceManager;
    private DateTimeOffset _suppressElevatedFallbackUntil;

    public RuntimeDataAclRepairClient(
        IBackendClient backendClient,
        IBackendServiceManager backendServiceManager)
    {
        _backendClient = backendClient;
        _backendServiceManager = backendServiceManager;
    }

    public async Task<RuntimeDataAclRepairClientResult> RepairAsync(CancellationToken cancellationToken)
    {
        try
        {
            FrontendDebugLog.Info("RuntimeDataAclRepairClient", "Pipe runtime data ACL repair started.");
            await _backendClient.RepairRuntimeDataAclAsync(cancellationToken).ConfigureAwait(false);
            FrontendDebugLog.Info("RuntimeDataAclRepairClient", "Pipe runtime data ACL repair succeeded.");
            return new RuntimeDataAclRepairClientResult(true, false, "PIPE_OK", "Backend repaired the runtime data ACL.");
        }
        catch (BackendRequestException ex)
        {
            FrontendDebugLog.Error("RuntimeDataAclRepairClient", ex, "Pipe runtime data ACL repair returned failure.");
            return new RuntimeDataAclRepairClientResult(false, false, ex.ErrorCode ?? "PIPE_REPAIR_FAILED", ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RuntimeDataAclRepairClientResult(false, true, "CANCELLED", "Runtime data ACL repair was cancelled.");
        }
        catch (Exception ex) when (IsPipeUnavailableException(ex))
        {
            FrontendDebugLog.Warning("RuntimeDataAclRepairClient", $"Pipe runtime data ACL repair unavailable; falling back to elevated bootstrapper. Exception={ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            FrontendDebugLog.Warning("RuntimeDataAclRepairClient", $"Pipe runtime data ACL repair failed unexpectedly; falling back to elevated bootstrapper. Exception={ex.GetType().Name}: {ex.Message}");
        }

        if (DateTimeOffset.UtcNow < _suppressElevatedFallbackUntil)
        {
            return new RuntimeDataAclRepairClientResult(
                false,
                true,
                "ELEVATION_RECENTLY_CANCELLED",
                "Elevated runtime data ACL repair was recently cancelled.");
        }

        var fallbackResult = await _backendServiceManager
            .RepairRuntimeDataAclAsync(cancellationToken)
            .ConfigureAwait(false);

        if (fallbackResult.Cancelled)
        {
            _suppressElevatedFallbackUntil = DateTimeOffset.UtcNow.Add(CancelledPromptSuppression);
        }

        FrontendDebugLog.Info(
            "RuntimeDataAclRepairClient",
            $"Elevated runtime data ACL repair completed. Success={fallbackResult.Success}, Cancelled={fallbackResult.Cancelled}, Code={fallbackResult.Code}, Detail={fallbackResult.Detail}");

        return new RuntimeDataAclRepairClientResult(
            fallbackResult.Success,
            fallbackResult.Cancelled,
            fallbackResult.Code,
            fallbackResult.Detail);
    }

    private static bool IsPipeUnavailableException(Exception exception)
        => exception is TimeoutException
            or IOException
            or InvalidOperationException;
}
