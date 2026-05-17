using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Catalogs and mutates special shell menu surfaces that are not normal shell verbs.
/// </summary>
public sealed class SpecialMenuService
{
    private const string ShellNewOrderPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew";
    private const string CommandStorePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";
    private const string GuidBlockedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    private const string IeRootPath = @"Software\Microsoft\Internet Explorer";
    private const string UserClassesPath = @"Software\Classes";
    private const string MachineClassesPath = @"SOFTWARE\Classes";
    private static readonly string DefaultSendToPath = Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Users\Default\AppData\Roaming\Microsoft\Windows\SendTo");
    private static readonly string DefaultWinXPath = Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Users\Default\AppData\Local\Microsoft\Windows\WinX");

    private readonly FileLogger _logger;

    public SpecialMenuService(FileLogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<SpecialMenuEntry>> GetSnapshotAsync(SpecialMenuKind kind, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        IReadOnlyList<SpecialMenuEntry> items = kind switch
        {
            SpecialMenuKind.ShellNew => GetShellNewItems(RequireUserContext(userContext)),
            SpecialMenuKind.SendTo => GetSendToItems(RequireUserContext(userContext)),
            SpecialMenuKind.WinX => GetWinXItems(RequireUserContext(userContext)),
            SpecialMenuKind.DragDrop => GetDragDropItems(),
            SpecialMenuKind.CommandStore => GetCommandStoreItems(),
            SpecialMenuKind.GuidBlock => GetGuidBlockItems(),
            SpecialMenuKind.InternetExplorer => GetIeItems(RequireUserContext(userContext)),
            _ => []
        };

        return Task.FromResult(items);
    }

    public async Task<PipeResponse> SetEnabledAsync(SpecialMenuEntry item, bool enabled, Guid? operationId, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            SpecialMenuEntry updated;
            try
            {
                updated = item.Kind switch
                {
                    SpecialMenuKind.ShellNew => SetShellNewEnabled(item, enabled, RequireUserContext(userContext)),
                    SpecialMenuKind.SendTo => SetFileSystemItemEnabled(item, enabled, GetSendToPath(RequireUserContext(userContext))),
                    SpecialMenuKind.WinX => SetFileSystemItemEnabled(item, enabled, GetWinXPath(RequireUserContext(userContext))),
                    SpecialMenuKind.DragDrop => SetDragDropEnabled(item, enabled),
                    SpecialMenuKind.InternetExplorer => SetRenameBackedRegistryItemEnabled(item, enabled, "MenuExt", "-MenuExt", RequireUserContext(userContext)),
                    SpecialMenuKind.CommandStore => SetCommandStoreEnabled(item, enabled),
                    SpecialMenuKind.GuidBlock => SetGuidBlockEnabled(item, enabled),
                    _ => throw new InvalidOperationException("This special menu item cannot be toggled.")
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                await _logger.LogAsync($"Permission denied when toggling {item.Kind} item {item.Id}: {ex.Message}", cancellationToken);

                if (item.Kind == SpecialMenuKind.DragDrop)
                {
                    return Failure(
                        "Permission denied: Unable to modify DragDrop handler. " +
                        "This operation requires administrator privileges to modify HKEY_CLASSES_ROOT registry keys. " +
                        "Please run ContextMenuMgr as administrator or check the backend service permissions.",
                        operationId);
                }

                return Failure($"Permission denied: {ex.Message}", operationId);
            }
            catch (SecurityException ex)
            {
                await _logger.LogAsync($"Security exception when toggling {item.Kind} item {item.Id}: {ex.Message}", cancellationToken);
                return Failure($"Security error: {ex.Message}. This operation may require elevated privileges.", operationId);
            }

            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"Set special menu item enabled. Kind={item.Kind}, Id={item.Id}, Enabled={enabled}.", cancellationToken);
            return Success("Special menu item updated.", updated, operationId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to toggle special menu item {item.Id}: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    public async Task<PipeResponse> CreateAsync(PipeRequest request, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            var item = request.SpecialKind switch
            {
                SpecialMenuKind.ShellNew when request.ShellNewCreate is not null => CreateShellNew(request.ShellNewCreate, RequireUserContext(userContext)),
                SpecialMenuKind.SendTo when request.SendToCreate is not null => CreateSendTo(request.SendToCreate, RequireUserContext(userContext)),
                SpecialMenuKind.WinX when request.WinXCreateGroup is not null => CreateWinXGroup(request.WinXCreateGroup, RequireUserContext(userContext)),
                SpecialMenuKind.WinX when request.WinXCreateEntry is not null => CreateWinXEntry(request.WinXCreateEntry, RequireUserContext(userContext)),
                SpecialMenuKind.DragDrop when request.DragDropCreate is not null => CreateDragDrop(request.DragDropCreate),
                SpecialMenuKind.CommandStore when request.SpecialItem is not null => CreateCommandStore(request.SpecialItem),
                SpecialMenuKind.GuidBlock when request.GuidBlockCreate is not null => CreateGuidBlock(request.GuidBlockCreate),
                SpecialMenuKind.InternetExplorer when request.IeMenuCreate is not null => CreateIe(request.IeMenuCreate, RequireUserContext(userContext)),
                _ => throw new InvalidOperationException("The create request was missing required data.")
            };

            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"Created special menu item. Kind={item.Kind}, Id={item.Id}.", cancellationToken);
            return Success("Special menu item created.", item, request.ClientOperationId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to create special menu item: {ex.Message}", cancellationToken);
            return Failure(ex.Message, request.ClientOperationId);
        }
    }

    public async Task<PipeResponse> UpdateAsync(PipeRequest request, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            var item = request.SpecialKind switch
            {
                SpecialMenuKind.ShellNew when request.ShellNewUpdate is not null => UpdateShellNew(request.ShellNewUpdate, RequireUserContext(userContext)),
                SpecialMenuKind.SendTo when request.SendToUpdate is not null => UpdateSendTo(request.SendToUpdate, RequireUserContext(userContext)),
                SpecialMenuKind.WinX when request.WinXUpdateEntry is not null => UpdateWinXEntry(request.WinXUpdateEntry, RequireUserContext(userContext)),
                SpecialMenuKind.DragDrop when request.DefaultDropEffect is not null => UpdateDefaultDropEffect(request.DefaultDropEffect.Value),
                SpecialMenuKind.InternetExplorer when request.IeMenuUpdate is not null => UpdateIe(request.IeMenuUpdate, RequireUserContext(userContext)),
                SpecialMenuKind.CommandStore when request.SpecialItem is not null => UpdateCommandStore(request.SpecialItem),
                _ => throw new InvalidOperationException("The update request was missing required data.")
            };

            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"Updated special menu item. Kind={item.Kind}, Id={item.Id}.", cancellationToken);
            return Success("Special menu item updated.", item, request.ClientOperationId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to update special menu item: {ex.Message}", cancellationToken);
            return Failure(ex.Message, request.ClientOperationId);
        }
    }

    public async Task<PipeResponse> DeleteAsync(SpecialMenuEntry item, Guid? operationId, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            switch (item.Kind)
            {
                case SpecialMenuKind.InternetExplorer:
                    DeleteRegistryTree(item.RegistryPath, RequireUserContext(userContext));
                    if (item.Metadata.TryGetValue("DisabledRegistryPath", out var ieDisabledPath))
                    {
                        DeleteRegistryTree(ieDisabledPath, RequireUserContext(userContext));
                    }

                    break;
                case SpecialMenuKind.ShellNew:
                    DeleteRegistryTree(item.RegistryPath, RequireUserContext(userContext));
                    if (item.Metadata.TryGetValue("DisabledRegistryPath", out var disabledPath))
                    {
                        DeleteRegistryTree(disabledPath, RequireUserContext(userContext));
                    }

                    break;
                case SpecialMenuKind.DragDrop:
                case SpecialMenuKind.CommandStore:
                    DeleteRegistryTree(item.RegistryPath);
                    break;
                case SpecialMenuKind.GuidBlock:
                    DeleteRegistryValue(Registry.LocalMachine, GuidBlockedPath, item.KeyName);
                    break;
                case SpecialMenuKind.SendTo:
                    DeleteFileSystemItem(item.Path, GetSendToPath(RequireUserContext(userContext)));
                    break;
                case SpecialMenuKind.WinX:
                    DeleteFileSystemItem(item.Path, GetWinXPath(RequireUserContext(userContext)));
                    break;
            }

            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"Deleted special menu item. Kind={item.Kind}, Id={item.Id}.", cancellationToken);
            return new PipeResponse { Success = true, Message = "Special menu item deleted.", ClientOperationId = operationId };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to delete special menu item {item.Id}: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    public async Task<PipeResponse> MoveAsync(PipeRequest request, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            SpecialMenuEntry item;
            if (request.ShellNewSort is not null)
            {
                item = MoveShellNew(request.ShellNewSort, RequireUserContext(userContext));
            }
            else if (request.WinXMove is not null)
            {
                item = MoveWinX(request.WinXMove, RequireUserContext(userContext));
            }
            else
            {
                throw new InvalidOperationException("The move request was missing required data.");
            }

            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"Moved special menu item. Kind={item.Kind}, Id={item.Id}.", cancellationToken);
            return Success("Special menu item moved.", item, request.ClientOperationId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to move special menu item: {ex.Message}", cancellationToken);
            return Failure(ex.Message, request.ClientOperationId);
        }
    }

    public async Task<PipeResponse> RestoreDefaultsAsync(SpecialMenuKind kind, string? groupName, Guid? operationId, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            if (kind == SpecialMenuKind.SendTo)
            {
                RestoreDirectory(DefaultSendToPath, GetSendToPath(RequireUserContext(userContext)));
            }
            else if (kind == SpecialMenuKind.WinX)
            {
                var source = string.IsNullOrWhiteSpace(groupName) ? DefaultWinXPath : Path.Combine(DefaultWinXPath, groupName);
                var winXPath = GetWinXPath(RequireUserContext(userContext));
                var destination = string.IsNullOrWhiteSpace(groupName) ? winXPath : Path.Combine(winXPath, groupName);
                EnsurePathUnder(destination, winXPath);
                RestoreDirectory(source, destination);
                foreach (var lnkPath in Directory.GetFiles(destination, "*.lnk", SearchOption.AllDirectories))
                {
                    WinXHasher.HashLnk(lnkPath);
                }
            }
            else
            {
                throw new InvalidOperationException("Defaults can only be restored for SendTo and Win+X.");
            }

            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"Restored defaults for {kind}.", cancellationToken);
            return new PipeResponse { Success = true, Message = "Defaults restored.", ClientOperationId = operationId };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Failed to restore defaults for {kind}: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    public async Task<PipeResponse> SetShellNewOrderLockAsync(bool locked, Guid? operationId, BackendUserContext? userContext, CancellationToken cancellationToken)
    {
        try
        {
            var userContextToUse = RequireUserContext(userContext);

            if (!EnableSecurityPrivilege())
            {
                await _logger.LogAsync("Unable to enable SeSecurityPrivilege. Proceeding without it.", cancellationToken);
            }

            RegistryKey? key;
            try
            {
                key = OpenShellNewOrderKeyForAcl(createIfMissing: locked);
            }
            catch (UnauthorizedAccessException)
            {
                await _logger.LogAsync($"Access denied when opening ShellNew ordering ACL for user {userContextToUse.Sid}", cancellationToken);
                return Failure("Permission denied: Cannot change ShellNew ordering lock. This operation requires permission to change the registry key ACL.", operationId);
            }
            catch (SecurityException ex)
            {
                await _logger.LogAsync($"Security exception when opening ShellNew ordering ACL: {ex.Message}", cancellationToken);
                return Failure($"Security error: {ex.Message}. This operation requires permission to change the registry key ACL.", operationId);
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Unable to open ShellNew ordering key ACL: {ex.Message}", cancellationToken);
                return Failure($"Unable to open ShellNew ordering key: {ex.Message}", operationId);
            }

            if (key is null)
            {
                await _logger.LogAsync($"ShellNew order unlock requested but key does not exist for user {userContextToUse.Sid}.", cancellationToken);
                return new PipeResponse { Success = true, Message = "ShellNew order lock updated successfully.", ClientOperationId = operationId };
            }

            using (key)
            {
                try
                {
                    SetShellNewLockState(key, locked);
                    await _logger.LogAsync($"ShellNew order lock set to {locked} for user {userContextToUse.Sid}.", cancellationToken);
                    return new PipeResponse { Success = true, Message = "ShellNew order lock updated successfully.", ClientOperationId = operationId };
                }
                catch (UnauthorizedAccessException)
                {
                    await _logger.LogAsync("Access denied when modifying ACL on ShellNew ordering key. Attempting ownership fallback.", cancellationToken);
                    try
                    {
                        EnsureShellNewOrderAclAccess(createIfMissing: locked);
                        SetShellNewOrderLock(locked, createIfMissing: locked);
                        await _logger.LogAsync($"ShellNew order lock set to {locked} for user {userContextToUse.Sid} after ownership fallback.", cancellationToken);
                        return new PipeResponse { Success = true, Message = "ShellNew order lock updated successfully.", ClientOperationId = operationId };
                    }
                    catch (Exception retryEx)
                    {
                        await _logger.LogAsync($"Ownership fallback failed when modifying ShellNew ordering ACL: {retryEx.Message}", cancellationToken);
                        return Failure("Permission denied: Cannot modify access control list (ACL) on ShellNew ordering key. This operation requires permission to change the registry key ACL.", operationId);
                    }
                }
                catch (SecurityException ex)
                {
                    await _logger.LogAsync($"Security exception when modifying ACL: {ex.Message}. Attempting ownership fallback.", cancellationToken);
                    try
                    {
                        EnsureShellNewOrderAclAccess(createIfMissing: locked);
                        SetShellNewOrderLock(locked, createIfMissing: locked);
                        await _logger.LogAsync($"ShellNew order lock set to {locked} for user {userContextToUse.Sid} after ownership fallback.", cancellationToken);
                        return new PipeResponse { Success = true, Message = "ShellNew order lock updated successfully.", ClientOperationId = operationId };
                    }
                    catch (Exception retryEx)
                    {
                        await _logger.LogAsync($"Ownership fallback failed after security exception: {retryEx.Message}", cancellationToken);
                        return Failure($"Security error modifying ACL: {ex.Message}. This operation requires permission to change the registry key ACL.", operationId);
                    }
                }
                catch (Exception ex)
                {
                    await _logger.LogAsync($"Failed to modify ShellNew order lock: {ex.Message}", cancellationToken);
                    return Failure(ex.Message, operationId);
                }
            }
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Unexpected error in SetShellNewOrderLockAsync: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    private static IReadOnlyList<SpecialMenuEntry> GetShellNewItems(BackendUserContext context)
    {
        var entries = new List<SpecialMenuEntry>();
        var seenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, int> ordered;
        try
        {
            ordered = GetShellNewOrderedExtensions(context);
        }
        catch
        {
            ordered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            EnableSecurityPrivilege();
        }
        catch
        {
        }

        var orderLocked = false;
        try
        {
            orderLocked = IsShellNewOrderLocked();
        }
        catch
        {
        }

        var specs = new List<ClassesRootSpec>(GetClassesRootSpecsSafe(context));

        if (specs.Count == 0)
        {
            try
            {
                var machineClasses = Registry.LocalMachine.OpenSubKey(MachineClassesPath, writable: false);
                if (machineClasses is not null)
                {
                    specs.Add(new ClassesRootSpec(machineClasses, @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes", "System"));
                }
            }
            catch
            {
            }
        }

        foreach (var spec in specs)
        {
            try
            {
                var candidates = spec.Root.GetSubKeyNames()
                    .Where(static name => name.StartsWith(".", StringComparison.Ordinal) || string.Equals(name, "Folder", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => ordered.TryGetValue(name, out var index) ? index : int.MaxValue)
                    .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase);

                foreach (var extension in candidates)
                {
                    try
                    {
                        foreach (var item in EnumerateShellNewForExtension(spec, extension))
                        {
                            if (!seenExtensions.Add(item.KeyName))
                            {
                                break;
                            }

                            entries.Add(item with
                            {
                                Metadata = item.Metadata.Concat(new[]
                                {
                                    new KeyValuePair<string, string>("OrderLocked", orderLocked.ToString())
                                }).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                            });
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        if (entries.Count == 0)
        {
            try
            {
                entries.AddRange(GetDefaultShellNewEntries());
            }
            catch
            {
            }
        }

        var sortableEntries = entries
            .OrderBy(item => ordered.TryGetValue(item.KeyName, out var index) ? index : int.MaxValue)
            .ThenBy(item => item.KeyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var beforeSeparator = sortableEntries.Where(static item => IsBeforeSeparatorEntry(item)).Select(static item => item with { CanMove = false }).ToArray();
        var afterSeparator = sortableEntries.Where(static item => !IsBeforeSeparatorEntry(item)).Select(item => item with { CanMove = orderLocked }).ToArray();
        return beforeSeparator
            .Concat(new[] { CreateShellNewSeparatorEntry() })
            .Concat(afterSeparator)
            .ToArray();
    }

    private static IEnumerable<SpecialMenuEntry> EnumerateShellNewForExtension(ClassesRootSpec spec, string extension)
    {
        using var extensionKey = spec.Root.OpenSubKey(extension, writable: false);
        if (extensionKey is null)
        {
            yield break;
        }

        var progId = extensionKey.GetValue(null)?.ToString();
        var parts = new List<(string KeyPath, bool Enabled)>
        {
            ($@"{extension}\ShellNew", true),
            ($@"{extension}\-ShellNew", false)
        };

        if (!string.IsNullOrWhiteSpace(progId))
        {
            parts.Add(($@"{extension}\{progId}\ShellNew", true));
            parts.Add(($@"{extension}\{progId}\-ShellNew", false));
        }

        foreach (var part in parts)
        {
            using var shellNewKey = spec.Root.OpenSubKey(part.KeyPath, writable: false);
            if (shellNewKey is null || !HasAnyValue(shellNewKey, "NullFile", "Data", "FileName", "Directory", "Command"))
            {
                continue;
            }

            var displayName = ResolveShellNewDisplayName(spec, shellNewKey, extension, progId);
            var (iconPath, iconIndex, iconFallback) = ResolveShellNewIcon(spec, shellNewKey, extension, progId);
            var registryPath = $@"{spec.RegistryPrefix}\{part.KeyPath}";
            var metadata = new Dictionary<string, string>
            {
                ["Extension"] = extension,
                ["ProgId"] = progId ?? string.Empty,
                ["CreationKind"] = GetShellNewCreationKind(shellNewKey),
                ["BeforeSeparator"] = IsShellNewBeforeSeparator(shellNewKey, extension).ToString(),
                ["DisabledRegistryPath"] = $@"{spec.RegistryPrefix}\{GetSiblingShellNewPath(part.KeyPath, part.Enabled ? "-ShellNew" : "ShellNew")}",
                ["SourceScope"] = spec.SourceScope
            };
            AddKnownShellNewLocalization(extension, metadata);
            yield return new SpecialMenuEntry
            {
                Id = EncodeId(SpecialMenuKind.ShellNew, registryPath),
                Kind = SpecialMenuKind.ShellNew,
                DisplayName = StripAcceleratorPrefix(string.IsNullOrWhiteSpace(displayName) ? extension : displayName),
                KeyName = extension,
                IsEnabled = part.Enabled,
                IconPath = iconPath,
                IconIndex = iconIndex,
                RegistryPath = registryPath,
                CommandText = shellNewKey.GetValue("Command")?.ToString(),
                TargetPath = iconFallback,
                CanMove = true,
                Notes = progId,
                Metadata = metadata
            };
        }
    }

    private static SpecialMenuEntry CreateShellNew(ShellNewCreateRequest request, BackendUserContext context)
    {
        var extension = NormalizeExtension(request.Extension);
        if (extension == ".")
        {
            throw new InvalidOperationException("The extension must include a file type, for example .txt.");
        }

        using var existingExtensionKey = GetClassesRootSpecs(context)
            .Select(spec => spec.Root.OpenSubKey(extension, writable: false))
            .FirstOrDefault(key => key is not null)
            ?? throw new InvalidOperationException($"The extension {extension} does not exist. Set its default app first.");
        var existingProgId = existingExtensionKey.GetValue(null)?.ToString();
        if (string.IsNullOrWhiteSpace(existingProgId) || Registry.ClassesRoot.OpenSubKey(existingProgId) is null)
        {
            throw new InvalidOperationException($"The extension {extension} does not have a valid ProgId/default app.");
        }

        using var userRoot = GetUserRegistryRoot(context, writable: true);
        using var userClasses = userRoot.CreateSubKey(UserClassesPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open current-user classes.");
        using var extensionKey = userClasses.CreateSubKey(extension, writable: true)
            ?? throw new InvalidOperationException($"Unable to create {extension}.");
        var progId = extensionKey.GetValue(null)?.ToString();
        if (string.IsNullOrWhiteSpace(progId))
        {
            extensionKey.SetValue(null, existingProgId, RegistryValueKind.String);
        }

        using var shellNewKey = extensionKey.CreateSubKey("ShellNew", writable: true)
            ?? throw new InvalidOperationException("Unable to create ShellNew key.");
        if (!string.IsNullOrEmpty(request.DataText))
        {
            shellNewKey.SetValue("Data", Encoding.UTF8.GetBytes(request.DataText), RegistryValueKind.Binary);
        }
        else
        {
            shellNewKey.SetValue("NullFile", string.Empty, RegistryValueKind.String);
        }

        var spec = GetUserClassesRootSpec(context);
        return EnumerateShellNewForExtension(spec, extension).First();
    }

    private static SpecialMenuEntry UpdateShellNew(ShellNewUpdateRequest request, BackendUserContext context)
    {
        var registryPath = DecodeId(request.Id);
        using var key = OpenRegistryKey(registryPath, writable: true, context)
            ?? throw new InvalidOperationException($"Unable to open {registryPath}.");
        var extension = registryPath.Split('\\').FirstOrDefault(static part => part.StartsWith('.')) ?? string.Empty;
        var spec = GetClassesRootSpecForPath(registryPath, context);
        var extensionKey = spec.Root.OpenSubKey(extension, writable: false);
        var progId = extensionKey?.GetValue(null)?.ToString();

        if (!string.IsNullOrWhiteSpace(request.DisplayName) && !string.IsNullOrWhiteSpace(progId))
        {
            using var progIdKey = spec.Root.CreateSubKey(progId, writable: true);
            progIdKey?.SetValue("FriendlyTypeName", request.DisplayName, RegistryValueKind.String);
        }

        SetOptionalValue(key, "IconPath", request.IconPath);
        SetOptionalValue(key, "Command", request.Command);
        if (request.DataText is not null)
        {
            key.DeleteValue("NullFile", throwOnMissingValue: false);
            key.SetValue("Data", Encoding.UTF8.GetBytes(request.DataText), RegistryValueKind.Binary);
        }

        if (request.BeforeSeparator is not null)
        {
            using var config = request.BeforeSeparator.Value
                ? key.CreateSubKey("Config", writable: true)
                : key.OpenSubKey("Config", writable: true);
            if (request.BeforeSeparator.Value)
            {
                config?.SetValue("BeforeSeparator", string.Empty, RegistryValueKind.String);
            }
            else
            {
                config?.DeleteValue("BeforeSeparator", throwOnMissingValue: false);
            }
        }

        return EnumerateShellNewForExtension(spec, extension).First();
    }

    private static SpecialMenuEntry SetShellNewEnabled(SpecialMenuEntry item, bool enabled, BackendUserContext context)
    {
        var source = item.RegistryPath ?? DecodeId(item.Id);
        var target = item.Metadata.TryGetValue("DisabledRegistryPath", out var disabledPath)
            ? disabledPath
            : source.Replace(enabled ? @"\-ShellNew" : @"\ShellNew", enabled ? @"\ShellNew" : @"\-ShellNew", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return item with { IsEnabled = enabled };
        }

        MoveRegistryKey(source, target, context);
        return item with
        {
            Id = EncodeId(SpecialMenuKind.ShellNew, target),
            IsEnabled = enabled,
            RegistryPath = target
        };
    }

    private static SpecialMenuEntry MoveShellNew(ShellNewSortRequest request, BackendUserContext context)
    {
        if (!IsShellNewOrderLocked())
        {
            throw new InvalidOperationException("ShellNew ordering requires the new-menu lock to be enabled first.");
        }

        var registryPath = DecodeId(request.Id);
        var extension = registryPath.Split('\\').FirstOrDefault(static part => part.StartsWith('.')) ?? string.Empty;
        var items = GetShellNewItems(context).Where(static item => item.CanMove).ToList();
        var index = items.FindIndex(item => string.Equals(item.KeyName, extension, StringComparison.OrdinalIgnoreCase));
        var target = request.MoveUp ? index - 1 : index + 1;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return items.ElementAtOrDefault(index) ?? throw new InvalidOperationException("ShellNew item was not found.");
        }

        (items[index], items[target]) = (items[target], items[index]);
        var relock = IsShellNewOrderLocked();
        try
        {
            if (relock)
            {
                SetShellNewOrderLock(locked: false, createIfMissing: false);
            }

            using var userRoot = GetUserRegistryRoot(context, writable: true);
            using var orderKey = userRoot.CreateSubKey(ShellNewOrderPath, writable: true)
                ?? throw new InvalidOperationException("Unable to open ShellNew order key.");
            orderKey.SetValue("Classes", items.Select(static item => item.KeyName).ToArray(), RegistryValueKind.MultiString);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("ShellNew ordering registry key is not writable. The backend needs permission to temporarily unlock the key, save the order, and lock it again.", ex);
        }
        catch (SecurityException ex)
        {
            throw new InvalidOperationException("ShellNew ordering registry key is protected by access control. The backend needs permission to temporarily unlock the key, save the order, and lock it again.", ex);
        }
        finally
        {
            if (relock)
            {
                SetShellNewOrderLock(locked: true, createIfMissing: true);
            }
        }

        return items[target];
    }

    private static IReadOnlyList<SpecialMenuEntry> GetSendToItems(BackendUserContext context)
    {
        var sendToPath = GetSendToPath(context);
        if (string.IsNullOrWhiteSpace(sendToPath))
        {
            return [];
        }

        Directory.CreateDirectory(sendToPath);
        return Directory.EnumerateFileSystemEntries(sendToPath)
            .Where(static path => !string.Equals(Path.GetFileName(path), "desktop.ini", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => DesktopIniStore.GetLocalizedFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(CreateSendToEntry)
            .ToArray();
    }

    private static SpecialMenuEntry CreateSendTo(SendToCreateRequest request, BackendUserContext context)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.TargetPath))
        {
            throw new InvalidOperationException("SendTo items require a display name and target path.");
        }

        var sendToPath = GetSendToPath(context);
        Directory.CreateDirectory(sendToPath);
        var path = GetUniqueFilePath(Path.Combine(sendToPath, RemoveIllegalChars(request.DisplayName) + ".lnk"));
        ShortcutFile.Write(path, request.TargetPath, request.Arguments, request.WorkingDirectory, request.DisplayName, null, null);
        DesktopIniStore.SetLocalizedFileName(path, request.DisplayName);
        return CreateSendToEntry(path);
    }

    private static SpecialMenuEntry UpdateSendTo(SendToUpdateRequest request, BackendUserContext context)
    {
        var path = DecodeId(request.Id);
        EnsurePathUnder(path, GetSendToPath(context));
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException("The SendTo item no longer exists.");
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            DesktopIniStore.SetLocalizedFileName(path, request.DisplayName);
        }

        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var current = ShortcutFile.Read(path);
            ShortcutFile.Write(
                path,
                request.TargetPath ?? current.TargetPath,
                request.Arguments ?? current.Arguments,
                request.WorkingDirectory ?? current.WorkingDirectory,
                request.DisplayName ?? current.Description,
                request.IconPath ?? current.IconLocation,
                request.RunAsAdministrator);
        }

        return CreateSendToEntry(path);
    }

    private static SpecialMenuEntry CreateSendToEntry(string path)
    {
        ShortcutInfo? shortcut = null;
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            try { shortcut = ShortcutFile.Read(path); }
            catch { }
        }

        var displayName = DesktopIniStore.GetLocalizedFileName(path);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileNameWithoutExtension(path);
        }

        var attributes = File.GetAttributes(path);
        var (iconPath, iconIndex, iconFallback) = ResolveSendToIcon(path, shortcut);
        return new SpecialMenuEntry
        {
            Id = EncodeId(SpecialMenuKind.SendTo, path),
            Kind = SpecialMenuKind.SendTo,
            DisplayName = displayName,
            KeyName = Path.GetFileName(path),
            IsEnabled = (attributes & FileAttributes.Hidden) == 0,
            IconPath = iconPath,
            IconIndex = iconIndex,
            Path = path,
            TargetPath = iconFallback ?? shortcut?.TargetPath,
            Arguments = shortcut?.Arguments,
            WorkingDirectory = shortcut?.WorkingDirectory,
            Notes = shortcut?.RunAsAdministrator == true ? "Run as administrator" : null,
            Metadata = new Dictionary<string, string>
            {
                ["EntryType"] = Directory.Exists(path) ? "Directory" : Path.GetExtension(path).TrimStart('.')
            }
        };
    }

    private static (string? IconPath, int IconIndex, string? FallbackPath) ResolveSendToIcon(string path, ShortcutInfo? shortcut)
    {
        if (shortcut is not null)
        {
            var (shortcutIconPath, shortcutIconIndex) = ParseIconLocation(shortcut.IconLocation);
            if (string.IsNullOrWhiteSpace(shortcutIconPath))
            {
                shortcutIconPath = shortcut.TargetPath;
                shortcutIconIndex = 0;
            }

            var fallback = File.Exists(shortcut.TargetPath) || Directory.Exists(shortcut.TargetPath)
                ? shortcut.TargetPath
                : path;
            return (shortcutIconPath, shortcutIconIndex, fallback);
        }

        if (Directory.Exists(path))
        {
            return (path, 0, path);
        }

        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            using var extensionKey = Registry.ClassesRoot.OpenSubKey(extension, writable: false);
            var progId = extensionKey?.GetValue(null)?.ToString();
            if (!string.IsNullOrWhiteSpace(progId))
            {
                using var defaultIconKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\DefaultIcon", writable: false);
                var (defaultIconPath, defaultIconIndex) = ParseIconLocation(defaultIconKey?.GetValue(null)?.ToString());
                if (!string.IsNullOrWhiteSpace(defaultIconPath))
                {
                    return (defaultIconPath, defaultIconIndex, path);
                }
            }

            return (extension, 0, path);
        }

        return (path, 0, path);
    }

    private static IReadOnlyList<SpecialMenuEntry> GetWinXItems(BackendUserContext context)
    {
        var winXPath = GetWinXPath(context);
        if (!Directory.Exists(winXPath))
        {
            return [];
        }

        var result = new List<SpecialMenuEntry>();
        foreach (var groupPath in Directory.EnumerateDirectories(winXPath).OrderByDescending(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var groupName = Path.GetFileName(groupPath);
            result.Add(new SpecialMenuEntry
            {
                Id = EncodeId(SpecialMenuKind.WinX, groupPath),
                Kind = SpecialMenuKind.WinX,
                DisplayName = groupName,
                KeyName = groupName,
                IsEnabled = (File.GetAttributes(groupPath) & FileAttributes.Hidden) == 0,
                Path = groupPath,
                GroupName = groupName,
                CanEdit = false,
                CanMove = false,
                Metadata = new Dictionary<string, string> { ["EntryType"] = "Group" }
            });

            foreach (var lnkPath in Directory.EnumerateFiles(groupPath, "*.lnk").OrderByDescending(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                result.Add(CreateWinXEntryFromPath(lnkPath, groupName));
            }
        }

        return result;
    }

    private static SpecialMenuEntry CreateWinXGroup(WinXCreateGroupRequest request, BackendUserContext context)
    {
        var groupName = ValidatePlainDirectoryName(request.GroupName, "Win+X group name");
        var groupPath = GetUniqueDirectoryPath(Path.Combine(GetWinXPath(context), groupName));
        EnsurePathUnder(groupPath, GetWinXPath(context));
        Directory.CreateDirectory(groupPath);
        var iniPath = Path.Combine(groupPath, "desktop.ini");
        File.WriteAllText(iniPath, string.Empty, Encoding.Unicode);
        File.SetAttributes(groupPath, File.GetAttributes(groupPath) | FileAttributes.ReadOnly);
        File.SetAttributes(iniPath, File.GetAttributes(iniPath) | FileAttributes.Hidden | FileAttributes.System);
        return GetWinXItems(context).First(item => string.Equals(item.Path, groupPath, StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry CreateWinXEntry(WinXCreateEntryRequest request, BackendUserContext context)
    {
        var winXPath = GetWinXPath(context);
        var groupName = ValidatePlainDirectoryName(request.GroupName, "Win+X group name");
        var groupPath = Path.Combine(winXPath, groupName);
        EnsurePathUnder(groupPath, winXPath);
        if (!Directory.Exists(groupPath))
        {
            throw new InvalidOperationException($"Win+X group '{request.GroupName}' does not exist.");
        }

        var index = Directory.GetFiles(groupPath, "*.lnk").Length + 1;
        var fileName = $"{index:00} - {RemoveIllegalChars(Path.GetFileNameWithoutExtension(request.TargetPath))}.lnk";
        var path = GetUniqueFilePath(Path.Combine(groupPath, fileName));
        ShortcutFile.Write(path, request.TargetPath, request.Arguments, request.WorkingDirectory, request.DisplayName, null, null);
        DesktopIniStore.SetLocalizedFileName(path, request.DisplayName);
        WinXHasher.HashLnk(path);
        return CreateWinXEntryFromPath(path, groupName);
    }

    private static SpecialMenuEntry UpdateWinXEntry(WinXUpdateEntryRequest request, BackendUserContext context)
    {
        var path = DecodeId(request.Id);
        var winXPath = GetWinXPath(context);
        EnsurePathUnder(path, winXPath);
        var current = ShortcutFile.Read(path);
        var groupName = Path.GetFileName(Path.GetDirectoryName(path));
        if (!string.IsNullOrWhiteSpace(request.GroupName) && !string.Equals(request.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
        {
            var targetGroupName = ValidatePlainDirectoryName(request.GroupName, "Win+X group name");
            var destinationGroupPath = Path.Combine(winXPath, targetGroupName);
            EnsurePathUnder(destinationGroupPath, winXPath);
            if (!Directory.Exists(destinationGroupPath))
            {
                throw new InvalidOperationException($"Win+X group '{request.GroupName}' does not exist.");
            }

            path = GetUniqueFilePath(Path.Combine(destinationGroupPath, Path.GetFileName(path)));
            EnsurePathUnder(path, winXPath);
            File.Move(DecodeId(request.Id), path);
            groupName = targetGroupName;
        }

        ShortcutFile.Write(
            path,
            request.TargetPath ?? current.TargetPath,
            request.Arguments ?? current.Arguments,
            request.WorkingDirectory ?? current.WorkingDirectory,
            request.DisplayName ?? current.Description,
            current.IconLocation,
            request.RunAsAdministrator);

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            DesktopIniStore.SetLocalizedFileName(path, request.DisplayName);
        }

        WinXHasher.HashLnk(path);
        return CreateWinXEntryFromPath(path, groupName ?? string.Empty);
    }

    private static SpecialMenuEntry MoveWinX(WinXMoveRequest request, BackendUserContext context)
    {
        var path = DecodeId(request.Id);
        var winXPath = GetWinXPath(context);
        EnsurePathUnder(path, winXPath);
        var groupPath = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Invalid Win+X item path.");
        EnsurePathUnder(groupPath, winXPath);
        var groupPaths = Directory.GetDirectories(winXPath)
            .OrderByDescending(static item => Path.GetFileName(item), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var groupIndex = groupPaths.FindIndex(item => string.Equals(item, groupPath, StringComparison.OrdinalIgnoreCase));
        if (groupIndex < 0)
        {
            return CreateWinXEntryFromPath(path, Path.GetFileName(groupPath));
        }

        var displayPaths = GetWinXGroupDisplayPaths(groupPath);
        var index = displayPaths.FindIndex(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return CreateWinXEntryFromPath(path, Path.GetFileName(groupPath));
        }

        if (request.MoveUp)
        {
            if (index > 0)
            {
                (displayPaths[index - 1], displayPaths[index]) = (displayPaths[index], displayPaths[index - 1]);
                var movedPath = RewriteWinXGroupOrder(groupPath, displayPaths, path, winXPath);
                return CreateWinXEntryFromPath(movedPath, Path.GetFileName(groupPath));
            }

            if (groupIndex == 0)
            {
                return CreateWinXEntryFromPath(path, Path.GetFileName(groupPath));
            }

            var targetGroupPath = groupPaths[groupIndex - 1];
            var movedToPrevious = MoveWinXEntryToGroup(path, groupPath, targetGroupPath, insertAtTop: false, winXPath);
            return CreateWinXEntryFromPath(movedToPrevious, Path.GetFileName(targetGroupPath));
        }

        if (index < displayPaths.Count - 1)
        {
            (displayPaths[index], displayPaths[index + 1]) = (displayPaths[index + 1], displayPaths[index]);
            var movedPath = RewriteWinXGroupOrder(groupPath, displayPaths, path, winXPath);
            return CreateWinXEntryFromPath(movedPath, Path.GetFileName(groupPath));
        }

        if (groupIndex >= groupPaths.Count - 1)
        {
            return CreateWinXEntryFromPath(path, Path.GetFileName(groupPath));
        }

        var nextGroupPath = groupPaths[groupIndex + 1];
        var movedToNext = MoveWinXEntryToGroup(path, groupPath, nextGroupPath, insertAtTop: true, winXPath);
        return CreateWinXEntryFromPath(movedToNext, Path.GetFileName(nextGroupPath));
    }

    private static SpecialMenuEntry CreateWinXEntryFromPath(string path, string groupName)
    {
        ShortcutInfo? shortcut = null;
        try { shortcut = ShortcutFile.Read(path); }
        catch { }

        var localizedDisplayName = DesktopIniStore.GetLocalizedFileName(path, translate: true);
        var displayName = !string.IsNullOrWhiteSpace(localizedDisplayName)
            ? localizedDisplayName
            : ShellMetadataResolver.ResolveResourceString(shortcut?.Description);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = shortcut?.Description;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileNameWithoutExtension(path);
        }

        displayName = StripWinXOrderPrefix(displayName);
        var (iconPath, iconIndex) = ParseIconLocation(shortcut?.IconLocation);

        return new SpecialMenuEntry
        {
            Id = EncodeId(SpecialMenuKind.WinX, path),
            Kind = SpecialMenuKind.WinX,
            DisplayName = displayName,
            KeyName = Path.GetFileName(path),
            IsEnabled = (File.GetAttributes(path) & FileAttributes.Hidden) == 0,
            Path = path,
            GroupName = groupName,
            TargetPath = shortcut?.TargetPath,
            Arguments = shortcut?.Arguments,
            WorkingDirectory = shortcut?.WorkingDirectory,
            IconPath = iconPath,
            IconIndex = iconIndex,
            CanMove = true,
            Notes = shortcut?.RunAsAdministrator == true ? "Run as administrator" : null,
            Metadata = new Dictionary<string, string> { ["EntryType"] = "Entry" }
        };
    }

    private static List<string> GetWinXGroupDisplayPaths(string groupPath) =>
        Directory.GetFiles(groupPath, "*.lnk")
            .OrderByDescending(static item => Path.GetFileName(item), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string MoveWinXEntryToGroup(string path, string sourceGroupPath, string targetGroupPath, bool insertAtTop, string winXPath)
    {
        EnsurePathUnder(path, winXPath);
        EnsurePathUnder(sourceGroupPath, winXPath);
        EnsurePathUnder(targetGroupPath, winXPath);
        var localizedName = TryGetRawLocalizedFileName(path);
        TryDeleteLocalizedFileName(path);
        var stagedPath = Path.Combine(targetGroupPath, $"{Guid.NewGuid():N}.lnk");
        File.Move(path, stagedPath);
        TrySetLocalizedFileName(stagedPath, localizedName);

        var sourceDisplay = GetWinXGroupDisplayPaths(sourceGroupPath)
            .Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        RewriteWinXGroupOrder(sourceGroupPath, sourceDisplay, null, winXPath);

        var targetDisplay = GetWinXGroupDisplayPaths(targetGroupPath)
            .Where(item => !string.Equals(item, stagedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (insertAtTop)
        {
            targetDisplay.Insert(0, stagedPath);
        }
        else
        {
            targetDisplay.Add(stagedPath);
        }

        return RewriteWinXGroupOrder(targetGroupPath, targetDisplay, stagedPath, winXPath);
    }

    private static string RewriteWinXGroupOrder(string groupPath, IReadOnlyList<string> displayPaths, string? trackedPath, string winXPath)
    {
        EnsurePathUnder(groupPath, winXPath);
        var trackedTemp = string.Empty;
        var tempItems = new List<(string TempPath, string Suffix, string LocalizedName)>();
        foreach (var path in displayPaths)
        {
            EnsurePathUnder(path, winXPath);
            if (!File.Exists(path))
            {
                continue;
            }

            var localizedName = TryGetRawLocalizedFileName(path);
            TryDeleteLocalizedFileName(path);
            var tempPath = Path.Combine(groupPath, $"{Guid.NewGuid():N}.lnk");
            File.Move(path, tempPath);
            if (string.Equals(path, trackedPath, StringComparison.OrdinalIgnoreCase))
            {
                trackedTemp = tempPath;
            }

            tempItems.Add((tempPath, GetWinXFileNameSuffix(path), localizedName));
        }

        var trackedResult = trackedTemp;
        for (var index = 0; index < tempItems.Count; index++)
        {
            var item = tempItems[index];
            var order = tempItems.Count - index;
            var targetPath = GetUniqueFilePath(Path.Combine(groupPath, $"{order:00} - {item.Suffix}"));
            File.Move(item.TempPath, targetPath);
            TrySetLocalizedFileName(targetPath, item.LocalizedName);
            WinXHasher.HashLnk(targetPath);
            if (string.Equals(item.TempPath, trackedTemp, StringComparison.OrdinalIgnoreCase))
            {
                trackedResult = targetPath;
            }
        }

        return string.IsNullOrWhiteSpace(trackedResult) ? trackedPath ?? string.Empty : trackedResult;
    }

    private static string TryGetRawLocalizedFileName(string path)
    {
        try
        {
            return DesktopIniStore.GetLocalizedFileName(path, translate: false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TrySetLocalizedFileName(string path, string localizedName)
    {
        if (string.IsNullOrWhiteSpace(localizedName))
        {
            return;
        }

        try
        {
            DesktopIniStore.SetLocalizedFileName(path, localizedName);
        }
        catch
        {
        }
    }

    private static void TryDeleteLocalizedFileName(string path)
    {
        try
        {
            DesktopIniStore.DeleteLocalizedFileName(path);
        }
        catch
        {
        }
    }

    private static string GetWinXFileNameSuffix(string path)
    {
        var fileName = Path.GetFileName(path);
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return RemoveIllegalChars(StripWinXOrderPrefix(withoutExtension)) + ".lnk";
    }

    private static IReadOnlyList<SpecialMenuEntry> GetDragDropItems()
    {
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Folder"] = @"Folder\shellex",
            ["Directory"] = @"Directory\shellex",
            ["Drive"] = @"Drive\shellex",
            ["AllFilesystemObjects"] = @"AllFilesystemObjects\shellex"
        };
        var result = new List<SpecialMenuEntry>();
        foreach (var group in groups)
        {
            foreach (var part in new[] { "DragDropHandlers", "-DragDropHandlers" })
            {
                using var handlers = Registry.ClassesRoot.OpenSubKey($@"{group.Value}\{part}", writable: false);
                if (handlers is null)
                {
                    continue;
                }

                foreach (var keyName in handlers.GetSubKeyNames())
                {
                    using var key = handlers.OpenSubKey(keyName);
                    var guidText = key?.GetValue(null)?.ToString();
                    if (!Guid.TryParse(guidText, out var guid) && !Guid.TryParse(keyName, out guid))
                    {
                        continue;
                    }

                    var displayName = GuidMetadataCatalog.GetDisplayName(guid) ?? keyName;
                    var icon = GuidMetadataCatalog.GetIconLocation(guid);
                    var registryPath = $@"HKEY_CLASSES_ROOT\{group.Value}\{part}\{keyName}";
                    result.Add(new SpecialMenuEntry
                    {
                        Id = EncodeId(SpecialMenuKind.DragDrop, registryPath),
                        Kind = SpecialMenuKind.DragDrop,
                        DisplayName = displayName,
                        KeyName = keyName,
                        IsEnabled = string.Equals(part, "DragDropHandlers", StringComparison.OrdinalIgnoreCase),
                        IconPath = icon.IconPath,
                        IconIndex = icon.IconIndex,
                        RegistryPath = registryPath,
                        GroupName = group.Key,
                        TargetPath = GuidMetadataCatalog.GetFilePath(guid),
                        Metadata = new Dictionary<string, string> { ["Guid"] = guid.ToString("B") }
                    });
                }
            }
        }

        result.Insert(0, new SpecialMenuEntry
        {
            Id = "dragdrop:default-drop-effect",
            Kind = SpecialMenuKind.DragDrop,
            DisplayName = "DefaultDropEffect",
            KeyName = "DefaultDropEffect",
            IsEnabled = true,
            CanDelete = false,
            CanEdit = true,
            Notes = GetDefaultDropEffect().ToString(),
            Metadata = new Dictionary<string, string> { ["EntryType"] = "DefaultDropEffect" }
        });
        return result;
    }

    private static SpecialMenuEntry CreateDragDrop(DragDropCreateRequest request)
    {
        if (!Guid.TryParse(request.GuidText, out var guid))
        {
            throw new InvalidOperationException("The GUID format is invalid.");
        }

        var shellExPath = request.GroupName switch
        {
            "Folder" => @"Folder\shellex",
            "Directory" => @"Directory\shellex",
            "Drive" => @"Drive\shellex",
            "AllFilesystemObjects" => @"AllFilesystemObjects\shellex",
            _ => throw new InvalidOperationException("Unknown drag-drop group.")
        };
        var path = $@"{shellExPath}\DragDropHandlers\{guid:B}";
        Registry.SetValue($@"HKEY_CLASSES_ROOT\{path}", string.Empty, guid.ToString("B"), RegistryValueKind.String);
        return GetDragDropItems().First(item => string.Equals(item.RegistryPath, $@"HKEY_CLASSES_ROOT\{path}", StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry UpdateDefaultDropEffect(DefaultDropEffect effect)
    {
        var value = (int)effect;
        foreach (var path in new[] { "*", "AllFilesystemObjects", "Folder", "Directory" })
        {
            Registry.SetValue($@"HKEY_CLASSES_ROOT\{path}", "DefaultDropEffect", value, RegistryValueKind.DWord);
        }

        return GetDragDropItems().First(static item => item.Id == "dragdrop:default-drop-effect");
    }

    private static IReadOnlyList<SpecialMenuEntry> GetCommandStoreItems()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CommandStorePath, writable: false);
        if (key is null)
        {
            return [];
        }

        return key.GetSubKeyNames()
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                using var itemKey = key.OpenSubKey(name);
                var displayName = itemKey is null ? name : ShellMetadataResolver.ResolveVerbDisplayName(itemKey, name);
                var command = itemKey?.OpenSubKey("command")?.GetValue(null)?.ToString();
                var icon = itemKey is null ? (null, 0) : ShellMetadataResolver.ResolveVerbIcon(itemKey, command);
                return new SpecialMenuEntry
                {
                    Id = EncodeId(SpecialMenuKind.CommandStore, $@"HKEY_LOCAL_MACHINE\{CommandStorePath}\{name}"),
                    Kind = SpecialMenuKind.CommandStore,
                    DisplayName = displayName,
                    KeyName = name,
                    IsEnabled = itemKey?.GetValue("LegacyDisable") is null,
                    IconPath = icon.Item1,
                    IconIndex = icon.Item2,
                    RegistryPath = $@"HKEY_LOCAL_MACHINE\{CommandStorePath}\{name}",
                    CommandText = command
                };
            })
            .ToArray();
    }

    private static SpecialMenuEntry CreateCommandStore(SpecialMenuEntry request)
    {
        if (string.IsNullOrWhiteSpace(request.KeyName) || string.IsNullOrWhiteSpace(request.CommandText))
        {
            throw new InvalidOperationException("CommandStore items require a key name and command.");
        }

        var path = $@"HKEY_LOCAL_MACHINE\{CommandStorePath}\{SanitizeKeyName(request.KeyName)}";
        Registry.SetValue(path, "MUIVerb", request.DisplayName, RegistryValueKind.String);
        if (!string.IsNullOrWhiteSpace(request.IconPath))
        {
            Registry.SetValue(path, "Icon", request.IconPath, RegistryValueKind.String);
        }

        Registry.SetValue($@"{path}\command", string.Empty, request.CommandText, RegistryValueKind.String);
        return GetCommandStoreItems().First(item => string.Equals(item.RegistryPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry UpdateCommandStore(SpecialMenuEntry request)
    {
        var path = DecodeId(request.Id);
        Registry.SetValue(path, "MUIVerb", request.DisplayName, RegistryValueKind.String);
        SetOptionalRegistryValue(path, "Icon", request.IconPath);
        if (!string.IsNullOrWhiteSpace(request.CommandText))
        {
            Registry.SetValue($@"{path}\command", string.Empty, request.CommandText, RegistryValueKind.String);
        }

        return GetCommandStoreItems().First(item => string.Equals(item.RegistryPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry SetCommandStoreEnabled(SpecialMenuEntry item, bool enabled)
    {
        using var key = OpenRegistryKey(item.RegistryPath ?? DecodeId(item.Id), writable: true)
            ?? throw new InvalidOperationException("Unable to open CommandStore item.");
        if (enabled)
        {
            key.DeleteValue("LegacyDisable", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue("LegacyDisable", string.Empty, RegistryValueKind.String);
        }

        return item with { IsEnabled = enabled };
    }

    private static IReadOnlyList<SpecialMenuEntry> GetGuidBlockItems()
    {
        using var key = Registry.LocalMachine.OpenSubKey(GuidBlockedPath, writable: false);
        if (key is null)
        {
            return [];
        }

        return key.GetValueNames()
            .Where(static name => Guid.TryParse(name, out _))
            .Select(name =>
            {
                var guid = Guid.Parse(name);
                var icon = GuidMetadataCatalog.GetIconLocation(guid);
                return new SpecialMenuEntry
                {
                    Id = EncodeId(SpecialMenuKind.GuidBlock, name),
                    Kind = SpecialMenuKind.GuidBlock,
                    DisplayName = GuidMetadataCatalog.GetDisplayName(guid) ?? key.GetValue(name)?.ToString() ?? name,
                    KeyName = name,
                    IsEnabled = true,
                    IconPath = icon.IconPath,
                    IconIndex = icon.IconIndex,
                    RegistryPath = $@"HKEY_LOCAL_MACHINE\{GuidBlockedPath}",
                    TargetPath = GuidMetadataCatalog.GetFilePath(guid),
                    Metadata = new Dictionary<string, string> { ["Guid"] = name }
                };
            })
            .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SpecialMenuEntry CreateGuidBlock(GuidBlockCreateRequest request)
    {
        if (!Guid.TryParse(request.GuidText, out var guid))
        {
            throw new InvalidOperationException("The GUID format is invalid.");
        }

        using var key = Registry.LocalMachine.CreateSubKey(GuidBlockedPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open Blocked shell extensions key.");
        key.SetValue(guid.ToString("B"), request.DisplayName ?? string.Empty, RegistryValueKind.String);
        return GetGuidBlockItems().First(item => string.Equals(item.KeyName, guid.ToString("B"), StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry SetGuidBlockEnabled(SpecialMenuEntry item, bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.LocalMachine.CreateSubKey(GuidBlockedPath, writable: true)
                ?? throw new InvalidOperationException("Unable to open Blocked shell extensions key.");
            key.SetValue(item.KeyName, item.DisplayName ?? string.Empty, RegistryValueKind.String);
        }
        else
        {
            DeleteRegistryValue(Registry.LocalMachine, GuidBlockedPath, item.KeyName);
        }

        return item with { IsEnabled = enabled };
    }

    private static IReadOnlyList<SpecialMenuEntry> GetIeItems(BackendUserContext context)
    {
        var result = new List<SpecialMenuEntry>();

        using var userBaseKey = GetUserRegistryRoot(context, writable: false);
        using var root = userBaseKey.OpenSubKey(IeRootPath, writable: false);

        if (root is not null)
        {
            PopulateIeItemsFromRoot(root, result);
        }

        return result.OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void PopulateIeItemsFromRoot(RegistryKey root, List<SpecialMenuEntry> result)
    {
        foreach (var part in new[] { "MenuExt", "-MenuExt" })
        {
            using var menuKey = root.OpenSubKey(part, writable: false);
            if (menuKey is null)
            {
                continue;
            }

            foreach (var keyName in menuKey.GetSubKeyNames())
            {
                using var itemKey = menuKey.OpenSubKey(keyName);
                var command = itemKey?.GetValue(null)?.ToString();
                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                result.Add(new SpecialMenuEntry
                {
                    Id = EncodeId(SpecialMenuKind.InternetExplorer, $@"HKEY_CURRENT_USER\{IeRootPath}\{part}\{keyName}"),
                    Kind = SpecialMenuKind.InternetExplorer,
                    DisplayName = keyName,
                    KeyName = keyName,
                    IsEnabled = string.Equals(part, "MenuExt", StringComparison.OrdinalIgnoreCase),
                    RegistryPath = $@"HKEY_CURRENT_USER\{IeRootPath}\{part}\{keyName}",
                    CommandText = command,
                    TargetPath = ShellMetadataResolver.ResolveResourceString(command)
                });
            }
        }
    }

    private static SpecialMenuEntry CreateIe(IeMenuCreateRequest request, BackendUserContext context)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Command))
        {
            throw new InvalidOperationException("IE menu items require a name and command.");
        }

        var path = $@"HKEY_CURRENT_USER\{IeRootPath}\MenuExt\{request.DisplayName.Replace("\\", string.Empty, StringComparison.Ordinal)}";
        using var key = CreateRegistryKey(path, context)
            ?? throw new InvalidOperationException($"Unable to create {path}.");
        key.SetValue(string.Empty, request.Command, RegistryValueKind.String);
        return GetIeItems(context).First(item => string.Equals(item.RegistryPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry UpdateIe(IeMenuUpdateRequest request, BackendUserContext context)
    {
        var path = DecodeId(request.Id);
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            var parent = path[..path.LastIndexOf('\\')];
            var newPath = $@"{parent}\{request.DisplayName.Replace("\\", string.Empty, StringComparison.Ordinal)}";
            MoveRegistryKey(path, newPath, context);
            path = newPath;
        }

        if (!string.IsNullOrWhiteSpace(request.Command))
        {
            using var key = OpenRegistryKey(path, writable: true, context)
                ?? throw new InvalidOperationException($"Unable to open {path}.");
            key.SetValue(string.Empty, request.Command, RegistryValueKind.String);
        }

        return GetIeItems(context).First(item => string.Equals(item.RegistryPath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static SpecialMenuEntry SetDragDropEnabled(SpecialMenuEntry item, bool enabled)
    {
        try
        {
            EnableSecurityPrivilege();
            EnableRestorePrivilege();
            EnableBackupPrivilege();

            return SetRenameBackedRegistryItemEnabled(item, enabled, "DragDropHandlers", "-DragDropHandlers");
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                var source = item.RegistryPath ?? DecodeId(item.Id);
                var target = source.Replace(
                    enabled ? @"\-DragDropHandlers\" : @"\DragDropHandlers\",
                    enabled ? @"\DragDropHandlers\" : @"\-DragDropHandlers\",
                    StringComparison.OrdinalIgnoreCase);

                MoveRegistryKeyWithElevation(source, target);
                return item with
                {
                    Id = EncodeId(item.Kind, target),
                    IsEnabled = enabled,
                    RegistryPath = target
                };
            }
            catch
            {
                throw;
            }
        }
    }

    private static void MoveRegistryKeyWithElevation(string sourcePath, string targetPath)
    {
        try
        {
            MoveRegistryKeyCore(sourcePath, targetPath, context: null);
        }
        catch (UnauthorizedAccessException)
        {
            EnableSecurityPrivilege();
            EnableRestorePrivilege();

            using var sourceKey = OpenRegistryKeyWithFallback(sourcePath, writable: false);
            if (sourceKey is null)
            {
                throw new InvalidOperationException($"Unable to open source registry key: {sourcePath}");
            }

            CreateRegistryKeyCopyWithFallback(sourceKey, targetPath);
            DeleteRegistryTreeWithFallback(sourcePath);
        }
    }

    private static RegistryKey? OpenRegistryKeyWithFallback(string? fullPath, bool writable)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var (root, subPath) = SplitRegistryPath(fullPath, context: null);

        try
        {
            return root.OpenSubKey(subPath, writable);
        }
        catch (UnauthorizedAccessException)
        {
            if (root == Registry.ClassesRoot)
            {
                try
                {
                    var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: false);
                    return machineRoot?.OpenSubKey(subPath, writable);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }

    private static void CreateRegistryKeyCopyWithFallback(RegistryKey source, string targetPath)
    {
        var (root, subPath) = SplitRegistryPath(targetPath, context: null);

        RegistryKey? target = null;
        try
        {
            target = root.CreateSubKey(subPath, writable: true);
        }
        catch (UnauthorizedAccessException)
        {
            if (root == Registry.ClassesRoot)
            {
                var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                if (machineRoot is not null)
                {
                    target = machineRoot.CreateSubKey(subPath, writable: true);
                }
            }
        }

        if (target is null)
        {
            throw new InvalidOperationException($"Unable to create target registry key: {targetPath}");
        }

        using (target)
        {
            foreach (var valueName in source.GetValueNames())
            {
                target.SetValue(valueName, source.GetValue(valueName)!, source.GetValueKind(valueName));
            }

            foreach (var subKeyName in source.GetSubKeyNames())
            {
                using var subKey = source.OpenSubKey(subKeyName, writable: false);
                if (subKey is not null)
                {
                    CreateRegistryKeyCopyWithFallback(subKey, $@"{targetPath}\{subKeyName}");
                }
            }
        }
    }

    private static void DeleteRegistryTreeWithFallback(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        try
        {
            DeleteRegistryTree(fullPath, context: null);
        }
        catch (UnauthorizedAccessException)
        {
            var (root, subPath) = SplitRegistryPath(fullPath, context: null);
            var parentPath = subPath.Contains('\\') ? subPath[..subPath.LastIndexOf('\\')] : string.Empty;
            var keyName = subPath.Contains('\\') ? subPath[(subPath.LastIndexOf('\\') + 1)..] : subPath;

            if (root == Registry.ClassesRoot)
            {
                var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                if (machineRoot is not null)
                {
                    using var parent = machineRoot.OpenSubKey(parentPath, writable: true);
                    parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
                }
            }
        }
    }

    private static bool EnableRestorePrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = Process.GetCurrentProcess().SafeHandle;
            if (!OpenProcessToken(processHandle.DangerousGetHandle(), TOKEN_ADJUST_PRIVILEGES | TOKEN_READ, out tokenHandle))
            {
                return false;
            }

            try
            {
                var privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = new LUID(),
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!LookupPrivilegeValue(null, SE_RESTORE_NAME, out privilege.Luid))
                {
                    return false;
                }

                var privileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = [privilege]
                };

                var length = 0u;
                if (!AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0u, IntPtr.Zero, ref length))
                {
                    return false;
                }

                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool EnableBackupPrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = Process.GetCurrentProcess().SafeHandle;
            if (!OpenProcessToken(processHandle.DangerousGetHandle(), TOKEN_ADJUST_PRIVILEGES | TOKEN_READ, out tokenHandle))
            {
                return false;
            }

            try
            {
                var privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = new LUID(),
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!LookupPrivilegeValue(null, SE_BACKUP_NAME, out privilege.Luid))
                {
                    return false;
                }

                var privileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = [privilege]
                };

                var length = 0u;
                if (!AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0u, IntPtr.Zero, ref length))
                {
                    return false;
                }

                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool EnableTakeOwnershipPrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = Process.GetCurrentProcess().SafeHandle;
            if (!OpenProcessToken(processHandle.DangerousGetHandle(), TOKEN_ADJUST_PRIVILEGES | TOKEN_READ, out tokenHandle))
            {
                return false;
            }

            try
            {
                var privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = new LUID(),
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!LookupPrivilegeValue(null, SE_TAKE_OWNERSHIP_NAME, out privilege.Luid))
                {
                    return false;
                }

                var privileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = [privilege]
                };

                var length = 0u;
                if (!AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0u, IntPtr.Zero, ref length))
                {
                    return false;
                }

                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    private const string SE_RESTORE_NAME = "SeRestorePrivilege";
    private const string SE_BACKUP_NAME = "SeBackupPrivilege";
    private const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";

    private static SpecialMenuEntry SetRenameBackedRegistryItemEnabled(SpecialMenuEntry item, bool enabled, string enabledPart, string disabledPart)
    {
        EnableSecurityPrivilege();
        return SetRenameBackedRegistryItemEnabled(item, enabled, enabledPart, disabledPart, context: null);
    }

    private static SpecialMenuEntry SetRenameBackedRegistryItemEnabled(SpecialMenuEntry item, bool enabled, string enabledPart, string disabledPart, BackendUserContext? context)
    {
        var source = item.RegistryPath ?? DecodeId(item.Id);
        var target = source.Replace(enabled ? $@"\{disabledPart}\" : $@"\{enabledPart}\", enabled ? $@"\{enabledPart}\" : $@"\{disabledPart}\", StringComparison.OrdinalIgnoreCase);
        MoveRegistryKey(source, target, context);
        return item with
        {
            Id = EncodeId(item.Kind, target),
            IsEnabled = enabled,
            RegistryPath = target
        };
    }

    private static SpecialMenuEntry SetFileSystemItemEnabled(SpecialMenuEntry item, bool enabled, string allowedRoot)
    {
        var path = item.Path ?? DecodeId(item.Id);
        EnsurePathUnder(path, allowedRoot);
        var attributes = File.GetAttributes(path);
        attributes = enabled ? attributes & ~FileAttributes.Hidden : attributes | FileAttributes.Hidden;
        File.SetAttributes(path, attributes);
        return item with { IsEnabled = enabled };
    }

    private static DefaultDropEffect GetDefaultDropEffect()
    {
        foreach (var path in new[] { "*", "AllFilesystemObjects", "Folder", "Directory" })
        {
            using var key = Registry.ClassesRoot.OpenSubKey(path, writable: false);
            if (key?.GetValue("DefaultDropEffect") is int value && Enum.IsDefined(typeof(DefaultDropEffect), value))
            {
                return (DefaultDropEffect)value;
            }
        }

        return DefaultDropEffect.Default;
    }

    private static bool HasAnyValue(RegistryKey key, params string[] valueNames) => valueNames.Any(name => key.GetValue(name) is not null);

    private static string ResolveShellNewDisplayName(ClassesRootSpec spec, RegistryKey shellNewKey, string extension, string? progId)
    {
        var menuText = ShellMetadataResolver.ResolveResourceString(shellNewKey.GetValue("MenuText")?.ToString());
        if (!string.IsNullOrWhiteSpace(menuText))
        {
            return menuText;
        }

        if (!string.IsNullOrWhiteSpace(progId))
        {
            using var progIdKey = spec.Root.OpenSubKey(progId, writable: false) ?? Registry.ClassesRoot.OpenSubKey(progId, writable: false);
            var friendly = ShellMetadataResolver.ResolveResourceString(progIdKey?.GetValue("FriendlyTypeName")?.ToString());
            if (!string.IsNullOrWhiteSpace(friendly))
            {
                return friendly;
            }

            var defaultName = ShellMetadataResolver.ResolveResourceString(progIdKey?.GetValue(null)?.ToString());
            if (!string.IsNullOrWhiteSpace(defaultName))
            {
                return defaultName;
            }
        }

        return extension;
    }

    private static (string? IconPath, int IconIndex, string? FallbackPath) ResolveShellNewIcon(ClassesRootSpec spec, RegistryKey shellNewKey, string extension, string? progId)
    {
        var icon = shellNewKey.GetValue("IconPath")?.ToString();
        if (!string.IsNullOrWhiteSpace(icon))
        {
            var (path, index) = ParseIconLocation(icon);
            return (path, index, extension);
        }

        if (string.Equals(extension, "Folder", StringComparison.OrdinalIgnoreCase))
        {
            return ("imageres.dll", -3, null);
        }

        if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ("shell32.dll", -16769, extension);
        }

        if (string.IsNullOrWhiteSpace(progId))
        {
            return (null, 0, extension);
        }

        using var iconKey = spec.Root.OpenSubKey($@"{progId}\DefaultIcon", writable: false) ?? Registry.ClassesRoot.OpenSubKey($@"{progId}\DefaultIcon", writable: false);
        var defaultIcon = iconKey?.GetValue(null)?.ToString();
        if (!string.IsNullOrWhiteSpace(defaultIcon))
        {
            var (path, index) = ParseIconLocation(defaultIcon);
            return (path, index, extension);
        }

        return (null, 0, extension);
    }

    private static string GetShellNewCreationKind(RegistryKey key)
    {
        foreach (var name in new[] { "NullFile", "Data", "FileName", "Directory", "Command" })
        {
            if (key.GetValue(name) is not null)
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static bool IsShellNewBeforeSeparator(RegistryKey key, string extension)
    {
        if (new[] { "Folder", ".library-ms", ".lnk" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        using var config = key.OpenSubKey("Config", writable: false);
        return config?.GetValue("BeforeSeparator") is not null;
    }

    private static bool IsBeforeSeparatorEntry(SpecialMenuEntry item) =>
        bool.TryParse(item.Metadata.GetValueOrDefault("BeforeSeparator"), out var beforeSeparator) && beforeSeparator;

    private static SpecialMenuEntry CreateShellNewSeparatorEntry() => new()
    {
        Id = $"{SpecialMenuKind.ShellNew}:Separator",
        Kind = SpecialMenuKind.ShellNew,
        DisplayName = "ShellNewSeparator",
        KeyName = "Separator",
        IsEnabled = true,
        CanEdit = false,
        CanDelete = false,
        CanMove = false,
        Metadata = new Dictionary<string, string>
        {
            ["EntryType"] = "Separator",
            ["LocalizationKey"] = "ShellNewSeparator"
        }
    };

    private static Dictionary<string, int> GetShellNewOrderedExtensions(BackendUserContext context)
    {
        using var userRoot = GetUserRegistryRoot(context, writable: false);
        using var key = userRoot.OpenSubKey(ShellNewOrderPath, writable: false);
        var classes = key?.GetValue("Classes") as string[] ?? [];
        return classes.Select((value, index) => (value, index)).ToDictionary(static item => item.value, static item => item.index, StringComparer.OrdinalIgnoreCase);
    }

    private static RegistryAccessRule CreateShellNewLockRule() =>
        new(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            RegistryRights.Delete | RegistryRights.WriteKey,
            AccessControlType.Deny);

    private static void SetShellNewOrderLock(bool locked, bool createIfMissing)
    {
        using var key = OpenShellNewOrderKeyForAcl(createIfMissing);
        if (key is null)
        {
            return;
        }

        SetShellNewLockState(key, locked);
    }

    private static void EnsureShellNewOrderAclAccess(bool createIfMissing)
    {
        if (createIfMissing)
        {
            using var key = Registry.CurrentUser.CreateSubKey(ShellNewOrderPath, writable: true);
        }

        using var identity = WindowsIdentity.GetCurrent();
        var currentUser = identity.User ?? throw new InvalidOperationException("Unable to resolve backend process identity.");

        EnableTakeOwnershipPrivilege();
        EnableRestorePrivilege();

        using (var ownerKey = Registry.CurrentUser.OpenSubKey(
            ShellNewOrderPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.TakeOwnership))
        {
            if (ownerKey is null)
            {
                return;
            }

            var security = ownerKey.GetAccessControl(AccessControlSections.Owner);
            security.SetOwner(currentUser);
            ownerKey.SetAccessControl(security);
        }

        using (var aclKey = Registry.CurrentUser.OpenSubKey(
            ShellNewOrderPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ChangePermissions))
        {
            if (aclKey is null)
            {
                return;
            }

            var security = aclKey.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new RegistryAccessRule(
                currentUser,
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            aclKey.SetAccessControl(security);
        }
    }

    private static RegistryKey? OpenShellNewOrderKeyForAcl(bool createIfMissing)
    {
        var key = Registry.CurrentUser.OpenSubKey(
            ShellNewOrderPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ChangePermissions);

        if (key is not null || !createIfMissing)
        {
            return key;
        }

        using var newKey = Registry.CurrentUser.CreateSubKey(ShellNewOrderPath, writable: true);
        newKey?.Dispose();

        return Registry.CurrentUser.OpenSubKey(
            ShellNewOrderPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ChangePermissions);
    }

    private static void SetShellNewLockState(RegistryKey key, bool locked)
    {
        var security = key.GetAccessControl(AccessControlSections.Access);
        RemoveShellNewLockRules(security);

        if (locked)
        {
            security.AddAccessRule(CreateShellNewLockRule());
        }

        key.SetAccessControl(security);

        if (locked)
        {
            var verifySecurity = key.GetAccessControl(AccessControlSections.Access);
            var lockRuleExists = verifySecurity.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<RegistryAccessRule>()
                .Any(IsShellNewLockRule);
            if (!lockRuleExists)
            {
                throw new InvalidOperationException("Failed to verify ShellNew lock rule after setting ACL.");
            }
        }
    }

    private static void RemoveShellNewLockRules(RegistrySecurity security)
    {
        foreach (RegistryAccessRule existing in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (existing.IsInherited || !IsShellNewLockRule(existing))
            {
                continue;
            }

            try
            {
                security.RemoveAccessRuleSpecific(existing);
            }
            catch
            {
            }

            security.RemoveAccessRule(existing);
        }
    }

    private static bool IsShellNewLockRule(RegistryAccessRule rule) =>
        rule.AccessControlType == AccessControlType.Deny
        && IsWorldSid(rule.IdentityReference)
        && rule.RegistryRights.HasFlag(RegistryRights.Delete)
        && rule.RegistryRights.HasFlag(RegistryRights.WriteKey);

    private static bool IsWorldSid(IdentityReference identity)
    {
        try
        {
            var sid = identity as SecurityIdentifier
                ?? identity.Translate(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return sid?.IsWellKnown(WellKnownSidType.WorldSid) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsShellNewOrderLocked()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellNewOrderPath, writable: false);
            if (key is null)
            {
                return false;
            }

            var security = key.GetAccessControl();
            foreach (RegistryAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount)))
            {
                if (rule.AccessControlType == AccessControlType.Deny)
                {
                    var identity = rule.IdentityReference.ToString();
                    if (identity.Equals("Everyone", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (System.Security.SecurityException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IsShellNewOrderLocked] SecurityException when reading ACL: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IsShellNewOrderLocked] UnauthorizedAccessException when reading ACL: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IsShellNewOrderLocked] Unexpected exception when reading ACL: {ex.Message}");
            return false;
        }
    }

    private static bool CanWriteShellNewOrder(BackendUserContext context)
    {
        try
        {
            using var userRoot = GetUserRegistryRoot(context, writable: false);
            using var key = userRoot.OpenSubKey(ShellNewOrderPath, writable: true);
            if (key is not null)
            {
                return true;
            }

            var parentPath = ShellNewOrderPath[..ShellNewOrderPath.LastIndexOf('\\')];
            using var parent = userRoot.OpenSubKey(parentPath, writable: true);
            return parent is not null;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<ClassesRootSpec> GetClassesRootSpecs(BackendUserContext context)
    {
        var specs = new List<ClassesRootSpec>();
        try
        {
            specs.Add(GetUserClassesRootSpec(context));
        }
        catch
        {
        }

        try
        {
            var machineClasses = Registry.LocalMachine.OpenSubKey(MachineClassesPath, writable: false);
            if (machineClasses is not null)
            {
                specs.Add(new ClassesRootSpec(machineClasses, @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes", "System"));
            }
        }
        catch
        {
        }

        return specs;
    }

    private static IReadOnlyList<ClassesRootSpec> GetClassesRootSpecsSafe(BackendUserContext context)
    {
        return GetClassesRootSpecs(context);
    }

    private static ClassesRootSpec GetUserClassesRootSpec(BackendUserContext context)
    {
        var userBaseKey = GetUserRegistryRoot(context, writable: true);
        var root = userBaseKey.OpenSubKey(UserClassesPath, writable: false)
            ?? userBaseKey.CreateSubKey(UserClassesPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open caller user classes.");

        return new ClassesRootSpec(root, $@"HKEY_USERS\{context.Sid}\Software\Classes", "User");
    }

    private static ClassesRootSpec GetClassesRootSpecForPath(string registryPath, BackendUserContext context)
    {
        foreach (var spec in GetClassesRootSpecs(context))
        {
            if (registryPath.StartsWith(spec.RegistryPrefix + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        return new ClassesRootSpec(Registry.ClassesRoot, "HKEY_CLASSES_ROOT", "Merged");
    }

    private static BackendUserContext RequireUserContext(BackendUserContext? context) =>
        context ?? throw new InvalidOperationException("This operation requires an interactive user context.");

    private static RegistryKey GetUserRegistryRoot(BackendUserContext context, bool writable)
    {
        if (string.IsNullOrWhiteSpace(context.Sid))
        {
            throw new InvalidOperationException("The frontend user SID is not available.");
        }

        return Registry.Users.OpenSubKey(context.Sid, writable)
            ?? throw new InvalidOperationException($"The registry hive for user {context.Sid} is not loaded.");
    }

    private static string GetSendToPath(BackendUserContext context) => context.GetSendToPath();

    private static string GetWinXPath(BackendUserContext context) => context.GetWinXPath();

    private static void AddKnownShellNewLocalization(string extension, IDictionary<string, string> metadata)
    {
        if (string.Equals(extension, "Folder", StringComparison.OrdinalIgnoreCase))
        {
            metadata["LocalizationKey"] = "ShellNewFolder";
        }
        else if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            metadata["LocalizationKey"] = "ShellNewShortcut";
        }
        else if (string.Equals(extension, ".library-ms", StringComparison.OrdinalIgnoreCase))
        {
            metadata["LocalizationKey"] = "ShellNewLibrary";
        }
    }

    private static string StripAcceleratorPrefix(string value) => value.Replace("&", string.Empty, StringComparison.Ordinal);

    private static string StripWinXOrderPrefix(string value)
    {
        var text = value.Trim();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '-')
            {
                continue;
            }

            var prefix = text[..index].Trim();
            if (IsWinXOrderPrefix(prefix))
            {
                var remainder = text[(index + 1)..].TrimStart(' ');
                var nextHyphen = remainder.IndexOf('-');
                if (nextHyphen > 0 && IsWinXOrderPrefix(remainder[..nextHyphen].Trim()))
                {
                    remainder = remainder[(nextHyphen + 1)..].TrimStart(' ');
                }

                return remainder.TrimStart('-');
            }

            break;
        }

        return value;
    }

    private static bool IsWinXOrderPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !char.IsDigit(prefix[0]))
        {
            return false;
        }

        var hasDigit = false;
        foreach (var c in prefix)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                continue;
            }

            return false;
        }

        return hasDigit;
    }

    private static (string? IconPath, int IconIndex) ParseIconLocation(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return (null, 0);
        }

        var value = Environment.ExpandEnvironmentVariables(iconLocation.Trim().Trim('"'));
        var commaIndex = value.LastIndexOf(',');
        if (commaIndex > 0 && int.TryParse(value[(commaIndex + 1)..].Trim(), out var iconIndex))
        {
            return (value[..commaIndex].Trim().Trim('"'), iconIndex);
        }

        return (value, 0);
    }

    private static void RestoreDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(source);
        }

        if (Directory.Exists(destination))
        {
            File.SetAttributes(destination, FileAttributes.Normal);
            Directory.Delete(destination, recursive: true);
        }

        CopyDirectory(source, destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        File.SetAttributes(destination, File.GetAttributes(source));
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void DeleteFileSystemItem(string? path, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsurePathUnder(path, allowedRoot);
        if (Directory.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            DesktopIniStore.DeleteLocalizedFileName(path);
            File.Delete(path);
        }
    }

    private static void MoveRegistryKey(string sourcePath, string targetPath, BackendUserContext? context = null)
    {
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            MoveRegistryKeyCore(sourcePath, targetPath, context);
        }
        catch (UnauthorizedAccessException)
        {
            EnableSecurityPrivilege();
            MoveRegistryKeyCore(sourcePath, targetPath, context);
        }
    }

    private static void MoveRegistryKeyCore(string sourcePath, string targetPath, BackendUserContext? context = null)
    {
        using var source = OpenRegistryKey(sourcePath, writable: false, context)
            ?? throw new InvalidOperationException($"Unable to open {sourcePath}.");
        CreateRegistryKeyCopy(source, targetPath, context);
        DeleteRegistryTree(sourcePath, context);
    }

    private static void CreateRegistryKeyCopy(RegistryKey source, string targetPath, BackendUserContext? context = null)
    {
        using var target = CreateRegistryKey(targetPath, context)
            ?? throw new InvalidOperationException($"Unable to create {targetPath}.");
        foreach (var valueName in source.GetValueNames())
        {
            target.SetValue(valueName, source.GetValue(valueName)!, source.GetValueKind(valueName));
        }

        foreach (var subKeyName in source.GetSubKeyNames())
        {
            using var subKey = source.OpenSubKey(subKeyName, writable: false);
            if (subKey is not null)
            {
                CreateRegistryKeyCopy(subKey, $@"{targetPath}\{subKeyName}", context);
            }
        }
    }

    private static void DeleteRegistryTree(string? fullPath, BackendUserContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        var (root, subPath) = SplitRegistryPath(fullPath, context);
        var parentPath = subPath.Contains('\\') ? subPath[..subPath.LastIndexOf('\\')] : string.Empty;
        var keyName = subPath.Contains('\\') ? subPath[(subPath.LastIndexOf('\\') + 1)..] : subPath;
        using var parent = root.OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
    }

    private static void DeleteRegistryValue(RegistryKey root, string subPath, string valueName)
    {
        using var key = root.OpenSubKey(subPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static RegistryKey? OpenRegistryKey(string? fullPath, bool writable, BackendUserContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var (root, subPath) = SplitRegistryPath(fullPath, context);
        return root.OpenSubKey(subPath, writable);
    }

    private static RegistryKey? CreateRegistryKey(string fullPath, BackendUserContext? context = null)
    {
        var (root, subPath) = SplitRegistryPath(fullPath, context);
        return root.CreateSubKey(subPath, writable: true);
    }

    private static (RegistryKey Root, string SubPath) SplitRegistryPath(string fullPath, BackendUserContext? context = null)
    {
        var normalized = fullPath.Trim().Replace('/', '\\').Trim('\\');
        var separator = normalized.IndexOf('\\');
        var rootName = separator >= 0 ? normalized[..separator] : normalized;
        var subPath = separator >= 0 ? normalized[(separator + 1)..] : string.Empty;
        if (rootName.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) || rootName.Equals("HKCU", StringComparison.OrdinalIgnoreCase))
        {
            if (context is null)
            {
                throw new InvalidOperationException("HKEY_CURRENT_USER operations require a caller user context.");
            }

            return (Registry.Users, $@"{context.Sid}\{subPath}");
        }

        if ((rootName.Equals("HKEY_USERS", StringComparison.OrdinalIgnoreCase) || rootName.Equals("HKU", StringComparison.OrdinalIgnoreCase))
            && context is not null
            && !subPath.Equals(context.Sid, StringComparison.OrdinalIgnoreCase)
            && !subPath.StartsWith(context.Sid + "\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("The registry path is outside the authenticated caller user hive.");
        }

        var root = rootName.ToUpperInvariant() switch
        {
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_USERS" or "HKU" => Registry.Users,
            _ => throw new InvalidOperationException($"Unsupported registry root: {rootName}")
        };

        return (root, subPath);
    }

    private static string GetSiblingShellNewPath(string keyPath, string siblingName)
    {
        var parent = keyPath[..keyPath.LastIndexOf('\\')];
        return $@"{parent}\{siblingName}";
    }

    private static string NormalizeExtension(string value)
    {
        var extension = value.Trim();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return extension.ToLowerInvariant();
    }

    private static string GetFullNormalizedPath(string path) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    private static void EnsurePathUnder(string path, string root)
    {
        var fullPath = GetFullNormalizedPath(path);
        var fullRoot = GetFullNormalizedPath(root);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path is outside the allowed user menu root: {fullPath}");
        }
    }

    private static string ValidatePlainDirectoryName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || value == "."
            || value == ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException($"{label} must be a simple directory name.");
        }

        return value.Trim();
    }

    private static string RemoveIllegalChars(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['\\', ':']))
        {
            fileName = fileName.Replace(c.ToString(), string.Empty, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(fileName) ? "Item" : fileName.Trim();
    }

    private static string SanitizeKeyName(string keyName) => keyName.Replace("\\", string.Empty, StringComparison.Ordinal).Trim();

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name}{index}{extension}");
            index++;
        }
        while (File.Exists(candidate) || Directory.Exists(candidate));

        return candidate;
    }

    private static string GetUniqueDirectoryPath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return path;
        }

        var parent = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(parent, $"{name}{index}");
            index++;
        }
        while (Directory.Exists(candidate) || File.Exists(candidate));

        return candidate;
    }

    private static void SetOptionalValue(RegistryKey key, string valueName, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(valueName, value, RegistryValueKind.String);
        }
    }

    private static void SetOptionalRegistryValue(string path, string valueName, string? value, BackendUserContext? context = null)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            using var key = OpenRegistryKey(path, writable: true, context);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = CreateRegistryKey(path, context)
                ?? throw new InvalidOperationException($"Unable to open {path}.");
            key.SetValue(valueName, value, RegistryValueKind.String);
        }
    }

    private static string EncodeId(SpecialMenuKind kind, string path) =>
        $"{kind}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(path))}";

    private static string DecodeId(string id)
    {
        var index = id.IndexOf(':');
        if (index < 0)
        {
            return id;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(id[(index + 1)..]));
    }

    private static PipeResponse Success(string message, SpecialMenuEntry item, Guid? operationId) => new()
    {
        Success = true,
        Message = message,
        SpecialItem = item,
        ClientOperationId = operationId
    };

    private static PipeResponse Failure(string message, Guid? operationId) => new()
    {
        Success = false,
        Message = message,
        ClientOperationId = operationId
    };

    private sealed record ClassesRootSpec(RegistryKey Root, string RegistryPrefix, string SourceScope);

    private static IReadOnlyList<SpecialMenuEntry> GetDefaultShellNewEntries()
    {
        var entries = new List<SpecialMenuEntry>();
        var defaultExtensions = new[]
        {
            ".txt", ".docx", ".xlsx", ".pptx", ".zip", ".rar",
            ".bmp", ".jpg", ".png", ".gif", ".pdf",
            "Folder", ".lnk"
        };

        foreach (var extension in defaultExtensions)
        {
            var displayName = extension.Equals("Folder", StringComparison.OrdinalIgnoreCase) ? "文件夹" :
                           extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? "快捷方式" :
                           extension.TrimStart('.').ToUpperInvariant();

            entries.Add(new SpecialMenuEntry
            {
                Id = $"{SpecialMenuKind.ShellNew}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(extension))}",
                Kind = SpecialMenuKind.ShellNew,
                DisplayName = displayName,
                KeyName = extension,
                IsEnabled = true,
                CanEdit = false,
                CanDelete = false,
                CanMove = false,
                Metadata = new Dictionary<string, string>
                {
                    ["Extension"] = extension,
                    ["SourceScope"] = "Default",
                    ["OrderLocked"] = "false",
                    ["Warning"] = "User registry not accessible - showing defaults"
                }
            });
        }

        return entries;
    }

    private static bool EnableSecurityPrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = Process.GetCurrentProcess().SafeHandle;
            if (!OpenProcessToken(processHandle.DangerousGetHandle(), TOKEN_ADJUST_PRIVILEGES | TOKEN_READ, out tokenHandle))
            {
                return false;
            }

            try
            {
                var privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = new LUID(),
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!LookupPrivilegeValue(null, SE_SECURITY_NAME, out privilege.Luid))
                {
                    return false;
                }

                var privileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = [privilege]
                };

                var length = 0u;
                if (!AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0u, IntPtr.Zero, ref length))
                {
                    return false;
                }

                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_READ = 0x0008;
    private const uint ERROR_NOT_ALL_ASSIGNED = 1300;

    private const string SE_SECURITY_NAME = "SeSecurityPrivilege";

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? systemName, string privilegeName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, ref uint returnLength);
}
