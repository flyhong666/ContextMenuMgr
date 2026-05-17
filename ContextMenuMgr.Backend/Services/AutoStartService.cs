using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Manages auto-start settings for the frontend application.
/// All operations use the correct user context to ensure multi-user support.
/// </summary>
public sealed class AutoStartService
{
    private const string PolicyKeyPath = @"Software\ContextMenuMgr\Frontend";
    private const string PolicyValueName = "StartWithWindows";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ContextMenuManagerPlus.TrayHost";

    private readonly FileLogger _logger;

    public AutoStartService(FileLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets auto start enabled Async.
    /// </summary>
    public async Task<PipeResponse> SetAutoStartEnabledAsync(
        bool enabled,
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (userContext is null)
            {
                return Failure("User context is required for auto-start configuration.", operationId);
            }

            using var userBaseKey = OpenUserRegistryRoot(userContext, writable: true);

            using var policyKey = userBaseKey.OpenSubKey(PolicyKeyPath, writable: true)
                               ?? userBaseKey.CreateSubKey(PolicyKeyPath, writable: true);
            if (policyKey is not null)
            {
                policyKey.SetValue(PolicyValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            }

            // Clear Run key (we don't auto-add to Run key anymore)
            using var runKey = userBaseKey.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);

            await _logger.LogAsync($"Auto-start set to {enabled} for {userContext.Sid}.", cancellationToken);
            return new PipeResponse
            {
                Success = true,
                Message = $"Auto-start {(enabled ? "enabled" : "disabled")} successfully.",
                ClientOperationId = operationId,
                AutoStartEnabled = enabled
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            await _logger.LogAsync($"Permission denied setting auto-start: {ex.Message}", cancellationToken);
            return Failure(
                "Cannot write to the registry key. The backend service may not have sufficient permissions. " +
                "Please ensure the backend service is running with adequate privileges or run as administrator.",
                operationId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to set auto-start: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    /// <summary>
    /// Gets auto start enabled Async.
    /// </summary>
    public async Task<PipeResponse> GetAutoStartEnabledAsync(
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (userContext is null)
            {
                return new PipeResponse
                {
                    Success = true,
                    Message = "No user context provided.",
                    ClientOperationId = operationId,
                    AutoStartEnabled = false
                };
            }

            bool isEnabled = false;

            using var userBaseKey = OpenUserRegistryRoot(userContext, writable: false);

            // Check policy key first
            using var policyKey = userBaseKey.OpenSubKey(PolicyKeyPath, writable: false);
            if (policyKey?.GetValue(PolicyValueName) is int intValue)
            {
                isEnabled = intValue != 0;
            }

            // Fallback to Run key check
            if (!isEnabled)
            {
                using var runKey = userBaseKey.OpenSubKey(RunKeyPath, writable: false);
                isEnabled = runKey?.GetValue(RunValueName) is string command
                            && !string.IsNullOrWhiteSpace(command);
            }

            await _logger.LogAsync($"Auto-start status retrieved: {isEnabled} for {userContext.Sid}.", cancellationToken);
            
            return new PipeResponse
            {
                Success = true,
                Message = $"Auto-start is currently {(isEnabled ? "enabled" : "disabled")}.",
                ClientOperationId = operationId,
                AutoStartEnabled = isEnabled
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to get auto-start status: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    private static PipeResponse Failure(string message, Guid? operationId) => new()
    {
        Success = false,
        Message = message,
        ClientOperationId = operationId
    };

    private static RegistryKey OpenUserRegistryRoot(BackendUserContext userContext, bool writable)
    {
        if (string.IsNullOrWhiteSpace(userContext.Sid))
        {
            throw new InvalidOperationException("The frontend user SID is not available.");
        }

        return Registry.Users.OpenSubKey(userContext.Sid, writable)
            ?? throw new InvalidOperationException($"The registry hive for user {userContext.Sid} is not loaded.");
    }
}
