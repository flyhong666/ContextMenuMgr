using System.Security.AccessControl;
using System.Security.Principal;
using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Manages Win11 context menu blocking entries in the registry.
/// All operations use the correct user context to ensure multi-user support.
/// </summary>
public sealed class Windows11BlocksService
{
    private const string UserBlockedPathSuffix = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    private const string MachineBlockedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private readonly FileLogger _logger;

    public Windows11BlocksService(FileLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a CLSID to the blocked list.
    /// </summary>
    public async Task<PipeResponse> SetWin11BlockedItemAsync(
        string handlerClsid,
        string displayName,
        bool blockMachine,
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(handlerClsid))
            {
                return Failure("Handler CLSID is required.", operationId);
            }

            var normalizedClsid = NormalizeGuid(handlerClsid);

            if (blockMachine)
            {
                await AddToMachineBlockedListAsync(normalizedClsid, displayName, cancellationToken);
            }

            if (userContext is not null)
            {
                await AddToUserBlockedListAsync(normalizedClsid, displayName, userContext, cancellationToken);
            }
            else
            {
                await _logger.LogAsync(RuntimeLogLevel.Warning, "No user context provided for user-level Win11 block operation.", cancellationToken);
            }

            await _logger.LogAsync($"Win11 context menu item blocked: {normalizedClsid} (Machine={blockMachine}, User={userContext is not null})", cancellationToken);
            return new PipeResponse
            {
                Success = true,
                Message = "Win11 context menu item blocked successfully.",
                ClientOperationId = operationId
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to block Win11 item: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    /// <summary>
    /// Removes a CLSID from the blocked list.
    /// </summary>
    public async Task<PipeResponse> RemoveWin11BlockedItemAsync(
        string handlerClsid,
        bool unblockMachine,
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(handlerClsid))
            {
                return Failure("Handler CLSID is required.", operationId);
            }

            var normalizedClsid = NormalizeGuid(handlerClsid);

            if (unblockMachine)
            {
                await RemoveFromMachineBlockedListAsync(normalizedClsid, cancellationToken);
            }

            if (userContext is not null)
            {
                await RemoveFromUserBlockedListAsync(normalizedClsid, userContext, cancellationToken);
            }
            else
            {
                await _logger.LogAsync(RuntimeLogLevel.Warning, "No user context provided for user-level Win11 unblock operation.", cancellationToken);
            }

            await _logger.LogAsync($"Win11 context menu item unblocked: {normalizedClsid} (Machine={unblockMachine}, User={userContext is not null})", cancellationToken);
            return new PipeResponse
            {
                Success = true,
                Message = "Win11 context menu item unblocked successfully.",
                ClientOperationId = operationId
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to unblock Win11 item: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    /// <summary>
    /// Gets all blocked items for the current user and machine.
    /// </summary>
    public async Task<PipeResponse> GetWin11BlockedItemsAsync(
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var machineItems = GetMachineBlockedItems();
            var userItems = userContext is not null
                ? GetUserBlockedItems(userContext)
                : new List<Win11BlockedItem>();

            var allItems = machineItems.Concat(userItems).ToArray();

            await _logger.LogAsync($"Retrieved {allItems.Length} Win11 blocked items (Machine={machineItems.Count}, User={userItems.Count})", cancellationToken);
            return new PipeResponse
            {
                Success = true,
                Message = $"Retrieved {allItems.Length} blocked items.",
                ClientOperationId = operationId,
                Win11BlockedItems = allItems
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to get Win11 blocked items: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    #region Machine-level operations

    private async Task AddToMachineBlockedListAsync(string clsid, string displayName, CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.CreateSubKey(MachineBlockedPath, writable: true);
        if (key is not null)
        {
            var regName = NormalizeGuid(clsid);
            key.SetValue(regName, displayName, RegistryValueKind.String);
            await _logger.LogAsync($"Added {regName} to machine blocked list.", cancellationToken);
        }
    }

    private async Task RemoveFromMachineBlockedListAsync(string clsid, CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(MachineBlockedPath, writable: true);
        if (key is not null)
        {
            var regName = NormalizeGuid(clsid);
            DeleteGuidValue(key, regName);
            await _logger.LogAsync($"Removed {regName} from machine blocked list.", cancellationToken);
        }
    }

    private IReadOnlyList<Win11BlockedItem> GetMachineBlockedItems()
    {
        var items = new List<Win11BlockedItem>();
        using var key = Registry.LocalMachine.OpenSubKey(MachineBlockedPath, writable: false);
        if (key is null)
        {
            return items;
        }

        foreach (var valueName in key.GetValueNames())
        {
            var displayName = key.GetValue(valueName)?.ToString() ?? string.Empty;
            items.Add(new Win11BlockedItem
            {
                Clsid = valueName,
                DisplayName = displayName,
                Scope = "Machine"
            });
        }

        return items;
    }

    #endregion

    #region User-level operations

    private async Task AddToUserBlockedListAsync(string clsid, string displayName, BackendUserContext userContext, CancellationToken cancellationToken)
    {
        try
        {
            using var key = OpenUserBlockedKey(userContext, writable: true, create: true);
            
            if (key is not null)
            {
                // Format GUID with braces like reference project does
                var regName = NormalizeGuid(clsid);
                key.SetValue(regName, displayName, RegistryValueKind.String);
                await _logger.LogAsync($"Added {regName} to user blocked list for {userContext.Sid}.", cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("Unable to open or create blocked list registry key.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Warning, $"Permission denied adding to user blocked list: {ex.Message}", cancellationToken);
            throw new InvalidOperationException(
                "Cannot write to the registry key. The backend service may not have sufficient permissions to modify the current user's registry. " +
                "Please ensure the backend service is running with adequate privileges or run the application as administrator.",
                ex);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to add to user blocked list: {ex.Message}", cancellationToken);
            throw;
        }
    }

    private async Task RemoveFromUserBlockedListAsync(string clsid, BackendUserContext userContext, CancellationToken cancellationToken)
    {
        try
        {
            using var key = OpenUserBlockedKey(userContext, writable: true, create: false);
            if (key is not null)
            {
                var regName = NormalizeGuid(clsid);
                DeleteGuidValue(key, regName);
                await _logger.LogAsync($"Removed {regName} from user blocked list for {userContext.Sid}.", cancellationToken);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Warning, $"Permission denied removing from user blocked list: {ex.Message}", cancellationToken);
            throw new InvalidOperationException(
                "Cannot write to the registry key. The backend service may not have sufficient permissions to modify the current user's registry. " +
                "Please ensure the backend service is running with adequate privileges or run the application as administrator.",
                ex);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to remove from user blocked list: {ex.Message}", cancellationToken);
            throw;
        }
    }

    private IReadOnlyList<Win11BlockedItem> GetUserBlockedItems(BackendUserContext userContext)
    {
        var items = new List<Win11BlockedItem>();
        
        try
        {
            using var key = OpenUserBlockedKey(userContext, writable: false, create: false);
            if (key is not null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var dn = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    items.Add(new Win11BlockedItem
                    {
                        Clsid = valueName,
                        DisplayName = dn,
                        Scope = "User"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogAsync(RuntimeLogLevel.Warning, $"Error reading user blocked items for {userContext.Sid}: {ex.Message}, returning empty list.", CancellationToken.None).GetAwaiter().GetResult();
        }

        return items;
    }

    #endregion

    #region Helper methods

    private static string NormalizeGuid(string guidText)
    {
        return Guid.TryParse(guidText, out var guid)
            ? guid.ToString("B")
            : guidText.Trim('{', '}');
    }

    private static void DeleteGuidValue(RegistryKey key, string normalizedClsid)
    {
        key.DeleteValue(normalizedClsid, throwOnMissingValue: false);

        // ContextMenuMgr writes {GUID} values; remove matching manual entries without braces too.
        foreach (var valueName in key.GetValueNames())
        {
            if (string.Equals(NormalizeGuid(valueName), normalizedClsid, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
    }

    private static RegistryKey? OpenUserBlockedKey(BackendUserContext userContext, bool writable, bool create)
    {
        if (string.IsNullOrWhiteSpace(userContext.Sid))
        {
            throw new InvalidOperationException("The frontend user SID is not available.");
        }

        var path = $@"{userContext.Sid}\{UserBlockedPathSuffix}";
        return create
            ? Registry.Users.CreateSubKey(path, writable)
            : Registry.Users.OpenSubKey(path, writable);
    }

    private static PipeResponse Failure(string message, Guid? operationId) => new()
    {
        Success = false,
        Message = message,
        ClientOperationId = operationId
    };

    #endregion
}
