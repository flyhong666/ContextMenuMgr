using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Manages the per-user Windows 11 classic context menu compatibility registry tweak.
/// </summary>
public sealed class Win11ClassicContextMenuService
{
    private const string TweakClsid = "{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string TweakKeyPath = $@"Software\Classes\CLSID\{TweakClsid}";
    private const string InprocServer32KeyPath = $@"{TweakKeyPath}\InprocServer32";
    private const string MissingUserContextMessage = "User context is required for Windows 11 context menu configuration.";
    private const string UnsupportedOsMessage = "This setting only applies to Windows 11.";

    private readonly FileLogger _logger;

    public Win11ClassicContextMenuService(FileLogger logger)
    {
        _logger = logger;
    }

    public async Task<PipeResponse> GetDisabledAsync(
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                await _logger.LogAsync("Win11ClassicContextMenuGet: Result=UnsupportedOs.", cancellationToken);
                return new PipeResponse
                {
                    Success = true,
                    Message = UnsupportedOsMessage,
                    ClientOperationId = operationId,
                    Win11ModernContextMenuDisabled = false
                };
            }

            if (userContext is null)
            {
                await _logger.LogAsync(RuntimeLogLevel.Warning, "Win11ClassicContextMenuGet: Sid=<null>, Result=Failed, Reason=MissingUserContext.", cancellationToken);
                return Failure(MissingUserContextMessage, operationId);
            }

            using var userRoot = OpenUserRegistryRoot(userContext, writable: false);
            using var inprocKey = userRoot.OpenSubKey(InprocServer32KeyPath, writable: false);
            var disabled = inprocKey is not null;
            var rawDefault = inprocKey?.GetValue(string.Empty, null);

            // This tweak is commonly represented by the presence of InprocServer32
            // with either an empty default value or no material default value.
            await _logger.LogAsync(
                $"Win11ClassicContextMenuGet: Sid={userContext.Sid}, Path={DiagnosticLogFormatter.FormatUserHivePath(userContext, InprocServer32KeyPath)}, Exists={inprocKey is not null}, DefaultValue={DiagnosticLogFormatter.FormatRegistryValueData(rawDefault)}, Result={disabled}.",
                cancellationToken);

            return new PipeResponse
            {
                Success = true,
                Message = disabled
                    ? "Windows 11 modern context menu is disabled."
                    : "Windows 11 modern context menu is enabled.",
                ClientOperationId = operationId,
                Win11ModernContextMenuDisabled = disabled
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to read Windows 11 context menu setting: {ex}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    public async Task<PipeResponse> SetDisabledAsync(
        bool disabled,
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                await _logger.LogAsync($"Win11ClassicContextMenuSet: Disabled={disabled}, Result=UnsupportedOs.", cancellationToken);
                return disabled
                    ? Failure(UnsupportedOsMessage, operationId)
                    : new PipeResponse
                    {
                        Success = true,
                        Message = UnsupportedOsMessage,
                        ClientOperationId = operationId,
                        Win11ModernContextMenuDisabled = false
                    };
            }

            if (userContext is null)
            {
                await _logger.LogAsync(RuntimeLogLevel.Warning, $"Win11ClassicContextMenuSet: Sid=<null>, Disabled={disabled}, Result=Failed, Reason=MissingUserContext.", cancellationToken);
                return Failure(MissingUserContextMessage, operationId);
            }

            using var userRoot = OpenUserRegistryRoot(userContext, writable: true);
            if (disabled)
            {
                using var inprocKey = userRoot.CreateSubKey(InprocServer32KeyPath, writable: true)
                    ?? throw new InvalidOperationException("Unable to create the Windows 11 context menu registry key.");
                inprocKey.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
                await _logger.LogAsync(
                    DiagnosticLogFormatter.BuildRegistryOperationLog(
                        "Win11ClassicContextMenuSetDefaultValue",
                        DiagnosticLogFormatter.FormatUserHivePath(userContext, InprocServer32KeyPath),
                        string.Empty,
                        RegistryValueKind.String,
                        string.Empty,
                        writable: true,
                        result: $"Success, Sid={userContext.Sid}"),
                    cancellationToken);
            }
            else
            {
                using var clsidRoot = userRoot.OpenSubKey(@"Software\Classes\CLSID", writable: true);
                clsidRoot?.DeleteSubKeyTree(TweakClsid, throwOnMissingSubKey: false);
                await _logger.LogAsync(
                    DiagnosticLogFormatter.BuildRegistryOperationLog(
                        "Win11ClassicContextMenuDeleteKey",
                        DiagnosticLogFormatter.FormatUserHivePath(userContext, TweakKeyPath),
                        null,
                        null,
                        null,
                        writable: true,
                        result: $"Success, Sid={userContext.Sid}"),
                    cancellationToken);
            }

            return new PipeResponse
            {
                Success = true,
                Message = "Windows 11 context menu setting saved. Restart File Explorer or sign out and back in for it to take effect.",
                ClientOperationId = operationId,
                Win11ModernContextMenuDisabled = disabled
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Warning, $"Permission denied updating Windows 11 context menu setting: {ex}", cancellationToken);
            return Failure(
                "Cannot write to the current user's registry hive. Please ensure the backend service is running with adequate privileges.",
                operationId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to update Windows 11 context menu setting: {ex}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    private RegistryKey OpenUserRegistryRoot(BackendUserContext userContext, bool writable)
    {
        if (string.IsNullOrWhiteSpace(userContext.Sid))
        {
            throw new InvalidOperationException("The frontend user SID is not available.");
        }

        var root = Registry.Users.OpenSubKey(userContext.Sid, writable)
            ?? throw new InvalidOperationException($"The registry hive for user {userContext.Sid} is not loaded.");
        _logger.LogFireAndForget($"OpenUserRegistryRoot: Sid={userContext.Sid}, Writable={writable}, Result=Success.");
        return root;
    }

    private static PipeResponse Failure(string message, Guid? operationId) => new()
    {
        Success = false,
        Message = message,
        ClientOperationId = operationId
    };
}
