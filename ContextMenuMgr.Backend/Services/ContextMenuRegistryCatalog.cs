using ContextMenuMgr.Contracts;
using Microsoft.Win32;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Represents the context Menu Registry Catalog.
/// </summary>
public sealed class ContextMenuRegistryCatalog
{
    private const string BlockedShellExtensionsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    internal const string Windows11MonitoredRootPath = @"PackagedCom\Windows11ContextMenu";

    private static readonly RegistryRootDescriptor[] MonitoredRoots =
    [
        new(ContextMenuCategory.AllFileSystemObjects, @"AllFilesystemObjects\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.AllFileSystemObjects, @"AllFilesystemObjects\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.AllFileSystemObjects, @"AllFilesystemObjects\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"AllFilesystemObjects\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.File, @"*\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.File, @"*\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.File, @"*\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"*\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Folder, @"Folder\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Folder, @"Folder\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Folder, @"Folder\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"Folder\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Directory, @"Directory\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Directory, @"Directory\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Directory, @"Directory\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"Directory\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.DirectoryBackground, @"Directory\Background\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.DirectoryBackground, @"Directory\Background\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.DirectoryBackground, @"Directory\Background\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"Directory\Background\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.DesktopBackground, @"DesktopBackground\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.DesktopBackground, @"DesktopBackground\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.DesktopBackground, @"DesktopBackground\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"DesktopBackground\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Drive, @"Drive\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Drive, @"Drive\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Drive, @"Drive\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"Drive\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Library, @"LibraryFolder\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Library, @"LibraryFolder\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Library, @"LibraryFolder\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"LibraryFolder\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Library, @"LibraryFolder\Background\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Library, @"LibraryFolder\Background\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Library, @"LibraryFolder\Background\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"LibraryFolder\Background\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Library, @"UserLibraryFolder\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Library, @"UserLibraryFolder\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Library, @"UserLibraryFolder\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"UserLibraryFolder\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.Computer, @"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.Computer, @"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.Computer, @"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.RecycleBin, @"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shell", ContextMenuEntryKind.ShellVerb),
        new(ContextMenuCategory.RecycleBin, @"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension),
        new(ContextMenuCategory.RecycleBin, @"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shellex\-ContextMenuHandlers", ContextMenuEntryKind.ShellExtension, @"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shellex\ContextMenuHandlers", true),
        new(ContextMenuCategory.RecycleBin, @"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shellex\PropertySheetHandlers", ContextMenuEntryKind.ShellExtension)
    ];

    private static readonly HashSet<string> MonitoredStableRootPaths = MonitoredRoots
        .Select(static root => root.StableRelativePath)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly FileLogger _logger;
    private readonly ContextMenuStateStore _stateStore;
    private readonly RegistryBackupService _backupService;
    private readonly BackendProtectionSettingsStore _protectionSettingsStore;
    private readonly Windows11ContextMenuCatalog _windows11Catalog;
    private volatile bool _interactiveSessionObserved;
    private volatile bool _interactiveSessionSnapshotSettled;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenuRegistryCatalog"/> class.
    /// </summary>
    public ContextMenuRegistryCatalog(
        FileLogger logger,
        ContextMenuStateStore stateStore,
        RegistryBackupService backupService,
        BackendProtectionSettingsStore protectionSettingsStore)
    {
        _logger = logger;
        _stateStore = stateStore;
        _backupService = backupService;
        _protectionSettingsStore = protectionSettingsStore;
        _windows11Catalog = new Windows11ContextMenuCatalog(logger);
    }

    /// <summary>
    /// Gets snapshot Async.
    /// </summary>
    public async Task<IReadOnlyList<ContextMenuEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return await BuildSnapshotAsync(
            await EnumerateActualEntriesAsync(cancellationToken),
            static state => MonitoredStableRootPaths.Contains(state.SourceRootPath) || state.IsWindows11ContextMenu,
            persistDiscoveredStates: true,
            persistSnapshotUpdates: true,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ContextMenuEntry>> GetReadOnlySnapshotAsync(CancellationToken cancellationToken = default)
    {
        return await BuildSnapshotAsync(
            await EnumerateActualEntriesAsync(cancellationToken),
            static state => MonitoredStableRootPaths.Contains(state.SourceRootPath) || state.IsWindows11ContextMenu,
            persistDiscoveredStates: false,
            persistSnapshotUpdates: false,
            cancellationToken);
    }

    /// <summary>
    /// Marks that an interactive user session is available for menu enumeration.
    /// </summary>
    public void MarkInteractiveSessionObserved()
    {
        _interactiveSessionObserved = true;
        _interactiveSessionSnapshotSettled = false;
    }

    /// <summary>
    /// Marks that the first snapshot taken after an interactive session became available
    /// was complete enough to be used as the runtime pruning baseline.
    /// </summary>
    public void MarkInteractiveSessionSnapshotSettled()
    {
        _interactiveSessionSnapshotSettled = true;
    }

    /// <summary>
    /// Gets scene Snapshot Async.
    /// </summary>
    public async Task<IReadOnlyList<ContextMenuEntry>> GetSceneSnapshotAsync(
        ContextMenuSceneKind sceneKind,
        string? scopeValue,
        CancellationToken cancellationToken = default)
    {
        var roots = GetSceneRoots(sceneKind, scopeValue).ToArray();
        if (roots.Length == 0)
        {
            return [];
        }

        var includedRootPaths = roots
            .Select(static root => root.StableRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var snapshot = await BuildSnapshotAsync(
            EnumerateEntries(roots),
            state => includedRootPaths.Contains(state.SourceRootPath),
            persistDiscoveredStates: false,
            persistSnapshotUpdates: false,
            cancellationToken);

        return snapshot
            .Select(static entry => entry with
            {
                IsPendingApproval = false,
                DetectedChangeKind = ContextMenuChangeKind.None,
                DetectedChangeDetails = null,
                HasConsistencyIssue = false,
                ConsistencyIssue = null
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<ContextMenuEntry>> GetWindows11SnapshotAsync(
        BackendUserContext? userContext,
        CancellationToken cancellationToken = default)
    {
        return _windows11Catalog.IsSupported
            ? await _windows11Catalog.EnumerateEntriesAsync(cancellationToken, userContext)
            : [];
    }

    private async Task<IReadOnlyList<ContextMenuEntry>> BuildSnapshotAsync(
        IEnumerable<ContextMenuEntry> actualEntriesSource,
        Func<PersistedContextMenuState, bool> includePersistedState,
        bool persistDiscoveredStates,
        bool persistSnapshotUpdates,
        CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        var hasBaseline = states.Count > 0;
        var actualEntries = new Dictionary<string, ContextMenuEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in actualEntriesSource)
        {
            if (actualEntries.TryGetValue(item.Id, out var existing))
            {
                // Some roots can describe the same logical menu entry more than once
                // (for example stable vs. mirrored shell extension locations).
                actualEntries[item.Id] = SelectPreferredActualEntry(existing, item);
                continue;
            }

            actualEntries[item.Id] = item;
        }

        var results = new List<ContextMenuEntry>();
        var dirty = false;
        var missingStateIdsToRemove = new List<string>();
        var preserveMissingStates = ShouldPreserveMissingStatesForCurrentSnapshot();
        var observedSourceRoots = actualEntries.Values
            .Select(static entry => entry.SourceRootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var observedWindows11Packages = actualEntries.Values
            .Where(static entry => entry.IsWindows11ContextMenu)
            .Select(static entry => ContextMenuApprovalIdentity.ExtractWin11PackageKey(entry.RegistryPath))
            .Where(static packageKey => !string.IsNullOrWhiteSpace(packageKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in actualEntries.Values.OrderBy(static item => item.Category).ThenBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            states.TryGetValue(entry.Id, out var state);
            var issue = GetConsistencyIssue(entry, state);
            var changeKind = GetDetectedChangeKind(entry, state, hasBaseline);
            var changeDetails = GetDetectedChangeDetails(entry, state, changeKind);
            var merged = entry with
            {
                IsPendingApproval = state?.IsPendingApproval ?? false,
                HasBackup = !string.IsNullOrWhiteSpace(state?.BackupFilePath),
                DeletedAtUtc = state?.DeletedAtUtc,
                IsPresentInRegistry = true,
                HasConsistencyIssue = !string.IsNullOrWhiteSpace(issue),
                ConsistencyIssue = issue,
                DetectedChangeKind = changeKind,
                DetectedChangeDetails = changeDetails
            };

            results.Add(merged);

            if (state is null)
            {
                if (persistDiscoveredStates && persistSnapshotUpdates && !hasBaseline)
                {
                    // The first persisted snapshot becomes the baseline that later
                    // runs compare against for change detection and approvals. Once
                    // a baseline exists, unknown entries must remain marked as Added
                    // until the user explicitly acknowledges them.
                    states[entry.Id] = PersistedContextMenuState.FromEntry(merged);
                    dirty = true;
                }

                continue;
            }

            if (persistSnapshotUpdates && state.ConsecutiveMissingSnapshots != 0)
            {
                state.ConsecutiveMissingSnapshots = 0;
                dirty = true;
            }

            if (persistSnapshotUpdates && changeKind == ContextMenuChangeKind.None)
            {
                dirty |= UpdateMetadata(state, merged);
            }
        }

        foreach (var state in states.Values
                     .Where(state => includePersistedState(state) && !actualEntries.ContainsKey(state.Id))
                     .OrderBy(static state => state.Category)
                     .ThenBy(static state => state.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (!state.IsDeleted && preserveMissingStates)
            {
                // Service startup can happen before all per-user classes and packaged
                // COM registrations are fully visible. Keeping the baseline alive
                // until the first fully interactive snapshot has completed prevents the entire
                // catalog from being
                // re-quarantined as "new" once those registrations appear.
                continue;
            }

            // External removals are intentionally silent in the UI, but they still
            // need to be removed from the persisted baseline. Otherwise a later
            // reinstall looks like an old known item instead of a genuinely new one.
            if (!state.IsDeleted)
            {
                if (!CanPruneMissingStateForCurrentSnapshot(state, observedSourceRoots, observedWindows11Packages))
                {
                    if (persistSnapshotUpdates && state.ConsecutiveMissingSnapshots != 0)
                    {
                        state.ConsecutiveMissingSnapshots = 0;
                        dirty = true;
                    }

                    continue;
                }

                if (!persistSnapshotUpdates)
                {
                    continue;
                }

                state.ConsecutiveMissingSnapshots++;
                dirty = true;
                if (state.ConsecutiveMissingSnapshots < 2)
                {
                    // Require a settled item to be absent in more than one stable
                    // snapshot before removing its baseline. This avoids startup
                    // races where shell registrations arrive one polling cycle later.
                    continue;
                }

                missingStateIdsToRemove.Add(state.Id);
                continue;
            }

            var issue = state.IsDeleted
                ? GetDeletedConsistencyIssue(state)
                : "The menu item is missing from the registry.";
            var changeKind = !state.IsDeleted && hasBaseline
                ? ContextMenuChangeKind.Removed
                : ContextMenuChangeKind.None;
            var changeDetails = changeKind == ContextMenuChangeKind.Removed
                ? "This item existed the last time the context menu catalog was scanned, but it is now missing from the registry."
                : null;

            results.Add(CreateVirtualEntry(state, issue, changeKind, changeDetails));
        }

        foreach (var stateId in missingStateIdsToRemove)
        {
            states.Remove(stateId);
        }

        PruneTransientStates(states);

        if (dirty)
        {
            await _stateStore.SaveAsync(states, cancellationToken);
        }

        return results;
    }

    private bool ShouldPreserveMissingStatesForCurrentSnapshot()
    {
        return !_interactiveSessionObserved || !_interactiveSessionSnapshotSettled;
    }

    private static bool CanPruneMissingStateForCurrentSnapshot(
        PersistedContextMenuState state,
        ISet<string> observedSourceRoots,
        ISet<string> observedWindows11Packages)
    {
        if (state.IsWindows11ContextMenu
            || string.Equals(state.SourceRootPath, Windows11MonitoredRootPath, StringComparison.OrdinalIgnoreCase))
        {
            var packageKey = ContextMenuApprovalIdentity.ExtractWin11PackageKey(state.RegistryPath);
            return !string.IsNullOrWhiteSpace(packageKey)
                   && observedWindows11Packages.Contains(packageKey);
        }

        return observedSourceRoots.Contains(state.SourceRootPath);
    }

    /// <summary>
    /// Applies desired State Async.
    /// </summary>
    public async Task<PipeResponse> ApplyDesiredStateAsync(
        string itemId,
        bool enable,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var item = snapshot.FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return CreateFailure($"Menu item '{itemId}' was not found.");
        }

        if (item.IsDeleted)
        {
            return CreateFailure($"Menu item '{item.DisplayName}' is deleted. Undo the deletion before changing its state.");
        }

        var stopwatch = Stopwatch.StartNew();
        await _logger.LogAsync(
            "ApplyDesiredStateStart: "
            + $"ItemId={item.Id}, DisplayName={item.DisplayName}, EntryKind={item.EntryKind}, "
            + $"IsWindows11ContextMenu={item.IsWindows11ContextMenu}, Enable={enable}, "
            + $"RegistryPath={item.RegistryPath}, BackendRegistryPath={item.BackendRegistryPath}, "
            + $"SourceRootPath={item.SourceRootPath}, HandlerClsid={item.HandlerClsid}, "
            + $"UserSid={DiagnosticLogFormatter.FormatSid(userContext)}.",
            cancellationToken);

        try
        {
            if (item.IsWindows11ContextMenu)
            {
                // Win11 packaged verbs do not use the classic shell verb/handler
                // write paths, so they are toggled through the blocked-extension list.
                if (!_windows11Catalog.SetEnabled(item.HandlerClsid ?? item.KeyName, item.DisplayName, userContext, enable))
                {
                    return CreateFailure($"Unable to update the Win11 context menu item '{item.DisplayName}'.");
                }
            }
            else
            switch (item.EntryKind)
            {
                case ContextMenuEntryKind.ShellVerb:
                    SetShellVerbEnabled(item.BackendRegistryPath, enable);
                    break;
                case ContextMenuEntryKind.ShellExtension:
                    SetShellExtensionEnabled(item, enable);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported entry kind: {item.EntryKind}");
            }

            var states = await _stateStore.LoadAsync(cancellationToken);
            var linkedEntries = GetStateLinkedEntries(snapshot, item);
            foreach (var linkedEntry in linkedEntries)
            {
                // One user gesture may affect several projected entries, so we keep
                // their persisted desired/observed state in sync here.
                var state = GetOrCreateState(states, linkedEntry);
                state.DesiredEnabled = enable;
                state.ObservedEnabled = enable;
                state.IsDeleted = false;
                state.IsPendingApproval = false;
                state.UpdatedAtUtc = DateTimeOffset.UtcNow;
                state.DeletedAtUtc = null;
                state.BackupFilePath = null;
            }

            if (item.EntryKind == ContextMenuEntryKind.ShellExtension)
            {
                UpdateLinkedShellExtensionPersistedStates(states, item.HandlerClsid, enable);
            }

            PruneTransientStates(states);
            await _stateStore.SaveAsync(states, cancellationToken);
            ShellChangeNotifier.NotifyAssociationsChanged();

            await _logger.LogAsync($"{(enable ? "Enabled" : "Disabled")} {item.DisplayName} ({item.RegistryPath}).", cancellationToken);

            var refreshed = (await GetSnapshotAsync(cancellationToken))
                .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase))
                ?? item with { IsEnabled = enable };
            stopwatch.Stop();
            await _logger.LogAsync(
                $"ApplyDesiredStateEnd: ItemId={item.Id}, DisplayName={item.DisplayName}, Enable={enable}, Result=Success, RefreshedIsEnabled={refreshed.IsEnabled}, ElapsedMs={stopwatch.ElapsedMilliseconds}.",
                cancellationToken);

            return new PipeResponse
            {
                Success = true,
                Message = $"{(enable ? "Enabled" : "Disabled")} {item.DisplayName}.",
                Item = refreshed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to update {item.DisplayName}: {ex}, ElapsedMs={stopwatch.ElapsedMilliseconds}", cancellationToken);
            return CreateFailure(ex.Message, item);
        }
    }

    /// <summary>
    /// Applies decision Async.
    /// </summary>
    public async Task<PipeResponse> ApplyDecisionAsync(
        string itemId,
        ContextMenuDecision decision,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var item = snapshot.FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));

        return decision switch
        {
            ContextMenuDecision.Allow => item is null
                ? CreateFailure($"Menu item '{itemId}' was not found.")
                : await ApplyDesiredStateAsync(itemId, enable: true, userContext, cancellationToken),
            ContextMenuDecision.Deny => item is null
                ? await RemovePendingApprovalStateAsync(itemId, cancellationToken)
                : await ApplyDesiredStateAsync(itemId, enable: false, userContext, cancellationToken),
            ContextMenuDecision.Remove => await RemovePendingApprovalItemAsync(item, itemId, cancellationToken),
            _ => CreateFailure("Unknown approval decision.")
        };
    }

    /// <summary>
    /// Executes acknowledge Item State Async.
    /// </summary>
    public async Task<PipeResponse> AcknowledgeItemStateAsync(string itemId, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        var actualEntry = (await EnumerateActualEntriesAsync(cancellationToken))
            .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));

        if (actualEntry is not null)
        {
            states.TryGetValue(itemId, out var previousState);
            if (previousState is not null && !string.IsNullOrWhiteSpace(previousState.BackupFilePath))
            {
                _backupService.DeleteBackupFile(previousState.BackupFilePath);
            }

            var state = GetOrCreateState(states, actualEntry);
            state.IsDeleted = false;
            state.IsPendingApproval = false;
            state.SuppressNextDetection = false;
            state.BackupFilePath = null;
            state.DeletedAtUtc = null;
            state.DesiredEnabled = actualEntry.IsEnabled;
            state.ObservedEnabled = actualEntry.IsEnabled;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            PruneTransientStates(states);
            await _stateStore.SaveAsync(states, cancellationToken);

            var refreshed = (await GetSnapshotAsync(cancellationToken))
                .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase))
                ?? actualEntry;

            return new PipeResponse
            {
                Success = true,
                Message = $"Synchronized {refreshed.DisplayName} with the current registry state.",
                Item = refreshed
            };
        }

        if (!states.TryGetValue(itemId, out var persistedState))
        {
            return new PipeResponse
            {
                Success = true,
                Message = $"Item '{itemId}' is already synchronized."
            };
        }

        if (!persistedState.IsDeleted)
        {
            states.Remove(itemId);
            PruneTransientStates(states);
            await _stateStore.SaveAsync(states, cancellationToken);

            return new PipeResponse
            {
                Success = true,
                Message = $"Removed stale state for {persistedState.DisplayName}."
            };
        }

        persistedState.IsPendingApproval = false;
        persistedState.SuppressNextDetection = false;
        persistedState.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(states, cancellationToken);

        return new PipeResponse
        {
            Success = true,
            Message = $"Acknowledged the current deleted state for {persistedState.DisplayName}.",
            Item = persistedState.ToDeletedEntry()
        };
    }

    /// <summary>
    /// Applies shell Attribute Async.
    /// </summary>
    public async Task<PipeResponse> ApplyShellAttributeAsync(
        string itemId,
        ContextMenuShellAttribute attribute,
        bool enable,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var item = snapshot.FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return CreateFailure($"Menu item '{itemId}' was not found.");
        }

        if (item.EntryKind != ContextMenuEntryKind.ShellVerb)
        {
            return CreateFailure("Only shell verb items support extra shell attributes.", item);
        }

        if (item.IsDeleted)
        {
            return CreateFailure($"Menu item '{item.DisplayName}' is deleted. Undo the deletion before editing its attributes.", item);
        }

        try
        {
            SetShellVerbAttribute(item.BackendRegistryPath, attribute, enable);

            var states = await _stateStore.LoadAsync(cancellationToken);
            var state = GetOrCreateState(states, item);
            state.OnlyWithShift = attribute == ContextMenuShellAttribute.OnlyWithShift ? enable : state.OnlyWithShift;
            state.OnlyInExplorer = attribute == ContextMenuShellAttribute.OnlyInExplorer ? enable : state.OnlyInExplorer;
            state.NoWorkingDirectory = attribute == ContextMenuShellAttribute.NoWorkingDirectory ? enable : state.NoWorkingDirectory;
            state.NeverDefault = attribute == ContextMenuShellAttribute.NeverDefault ? enable : state.NeverDefault;
            state.ShowAsDisabledIfHidden = attribute == ContextMenuShellAttribute.ShowAsDisabledIfHidden ? enable : state.ShowAsDisabledIfHidden;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _stateStore.SaveAsync(states, cancellationToken);
            ShellChangeNotifier.NotifyAssociationsChanged();

            await _logger.LogAsync($"Set attribute {attribute}={(enable ? "on" : "off")} for {item.DisplayName} ({item.RegistryPath}).", cancellationToken);

            var refreshed = (await GetSnapshotAsync(cancellationToken))
                .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase))
                ?? item;

            return new PipeResponse
            {
                Success = true,
                Message = $"Updated {attribute} for {item.DisplayName}.",
                Item = refreshed
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to set {attribute} for {item.DisplayName}: {ex}", cancellationToken);
            return CreateFailure(ex.Message, item);
        }
    }

    /// <summary>
    /// Applies display Text Async.
    /// </summary>
    public async Task<PipeResponse> ApplyDisplayTextAsync(string itemId, string textValue, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var item = snapshot.FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return CreateFailure($"Menu item '{itemId}' was not found.");
        }

        if (item.EntryKind != ContextMenuEntryKind.ShellVerb)
        {
            return CreateFailure("Only shell verb items support text changes.", item);
        }

        if (item.IsDeleted)
        {
            return CreateFailure($"Menu item '{item.DisplayName}' is deleted. Undo the deletion before changing its text.", item);
        }

        if (!CanEditDisplayText(item))
        {
            return CreateFailure("This menu item does not support text changes.", item);
        }

        if (string.IsNullOrWhiteSpace(textValue))
        {
            return CreateFailure("Menu text cannot be empty.", item);
        }

        var parsedText = ShellMetadataResolver.ResolveResourceString(textValue);
        if (string.IsNullOrWhiteSpace(parsedText))
        {
            return CreateFailure("The provided menu text could not be resolved.", item);
        }

        if (parsedText.Length >= 80)
        {
            return CreateFailure("The resolved menu text is too long.", item);
        }

        try
        {
            using var menuKey = OpenRegistryKey(item.BackendRegistryPath, writable: true)
                ?? throw new InvalidOperationException($"Unable to open {item.RegistryPath} for writing.");
            var oldValue = menuKey.GetValue("MUIVerb");
            menuKey.SetValue("MUIVerb", textValue, RegistryValueKind.String);
            await _logger.LogAsync(
                DiagnosticLogFormatter.BuildRegistryOperationLog(
                    "ApplyDisplayText",
                    item.BackendRegistryPath,
                    "MUIVerb",
                    RegistryValueKind.String,
                    textValue,
                    writable: true,
                    result: $"Success, OldValue={DiagnosticLogFormatter.FormatRegistryValueData(oldValue)}"),
                cancellationToken);

            var states = await _stateStore.LoadAsync(cancellationToken);
            var state = GetOrCreateState(states, item);
            state.DisplayName = NormalizeDisplayName(parsedText);
            state.EditableText = parsedText;
            state.ObservedEnabled = item.IsEnabled;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _stateStore.SaveAsync(states, cancellationToken);
            ShellChangeNotifier.NotifyAssociationsChanged();

            var refreshed = (await GetSnapshotAsync(cancellationToken))
                .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase))
                ?? item;

            return new PipeResponse
            {
                Success = true,
                Message = $"Updated display text for {item.DisplayName}.",
                Item = refreshed
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to update display text for {item.DisplayName}: {ex}", cancellationToken);
            return CreateFailure(ex.Message, item);
        }
    }

    /// <summary>
    /// Sets enhance Menu Item Enabled Async.
    /// </summary>
    public async Task<PipeResponse> SetEnhanceMenuItemEnabledAsync(
        string groupRegistryPath,
        string definitionXml,
        bool enable,
        string? cultureName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupRegistryPath))
        {
            return CreateFailure("The enhance-menu group registry path is required.");
        }

        if (string.IsNullOrWhiteSpace(definitionXml))
        {
            return CreateFailure("The enhance-menu item definition is required.");
        }

        try
        {
            var itemElement = XElement.Parse(definitionXml);
            var relativeGroupPath = NormalizeClassesRootRelativePath(groupRegistryPath)
                ?? throw new InvalidOperationException("The enhance-menu group path must point into HKCR.");
            var states = await _stateStore.LoadAsync(cancellationToken);
            var effectiveCultureName = NormalizeEnhanceCultureName(cultureName);

            if (itemElement.Attribute("KeyName") is not null)
            {
                SetEnhanceShellItemEnabled(relativeGroupPath, itemElement, enable, effectiveCultureName);
            }
            else if (itemElement.Element("Guid") is not null)
            {
                SetEnhanceShellExItemEnabled(relativeGroupPath, itemElement, enable);
            }
            else
            {
                throw new InvalidOperationException("The enhance-menu item definition could not be recognized.");
            }

            try
            {
                await SyncEnhanceMenuStateAsync(states, relativeGroupPath, itemElement, enable, cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.LogAsync(
                    RuntimeLogLevel.Warning,
                    $"Enhance menu registry update succeeded but state sync failed under {groupRegistryPath}. "
                    + $"KeyName={itemElement.Attribute("KeyName")?.Value?.Trim()}, Guid={itemElement.Element("Guid")?.Value?.Trim()}, Enable={enable}: {ex.Message}",
                    cancellationToken);
            }

            ShellChangeNotifier.NotifyAssociationsChanged();

            await _logger.LogAsync(
                $"{(enable ? "Enabled" : "Disabled")} enhance menu item under {groupRegistryPath}. "
                + $"KeyName={itemElement.Attribute("KeyName")?.Value?.Trim()}, Guid={itemElement.Element("Guid")?.Value?.Trim()}, Culture={effectiveCultureName}.",
                cancellationToken);

            return new PipeResponse
            {
                Success = true,
                Message = enable
                    ? "Enhance menu item enabled."
                    : "Enhance menu item disabled."
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to update enhance menu item: {ex}", cancellationToken);
            return CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Sets detailed edit rule value Async.
    /// </summary>
    public async Task<PipeResponse> SetDetailedEditRuleValueAsync(
        string? storageKind,
        string? path,
        string? section,
        string? keyName,
        string? valueKind,
        string? value,
        string? userSid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageKind))
        {
            return CreateFailure("The detailed edit rule storage kind is required.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateFailure("The detailed edit rule path is required.");
        }

        if (string.IsNullOrWhiteSpace(keyName))
        {
            return CreateFailure("The detailed edit rule key name is required.");
        }

        try
        {
            if (string.Equals(storageKind, "Registry", StringComparison.OrdinalIgnoreCase))
            {
                await _logger.LogAsync(
                    $"DetailedEditRegistryWriteStart: StorageKind={storageKind}, RawPath={path}, NormalizedPath={path.Replace('/', '\\').Trim()}, UserSid={userSid ?? "<null>"}, ValueName={DiagnosticLogFormatter.FormatRegistryValue(keyName)}, ValueKind={valueKind ?? RegistryValueKind.String.ToString()}, Operation={(value is null ? "DeleteValue" : "SetValue")}, ValueData={DiagnosticLogFormatter.FormatRegistryValueData(value)}.",
                    cancellationToken);
                WriteDetailedEditRegistryValue(path, keyName, valueKind, value, userSid);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported backend detailed edit storage kind: {storageKind}");
            }

            await _logger.LogAsync(
                $"Updated detailed edit rule value. Path={path}, KeyName={keyName}, ValueKind={valueKind}, Value={DiagnosticLogFormatter.FormatRegistryValueData(value)}.",
                cancellationToken);

            return new PipeResponse
            {
                Success = true,
                Message = "Detailed edit rule value updated."
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to update detailed edit rule value {path}\\{keyName}: {ex}", cancellationToken);
            return CreateFailure(ex.Message);
        }
    }

    private async Task SyncEnhanceMenuStateAsync(
        Dictionary<string, PersistedContextMenuState> states,
        string relativeGroupPath,
        XElement itemElement,
        bool enable,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var matchingEntry = FindEnhanceMenuEntry(snapshot, relativeGroupPath, itemElement);

        if (enable)
        {
            if (matchingEntry is not null)
            {
                var state = GetOrCreateState(states, matchingEntry);
                state.IsPendingApproval = false;
                state.IsDeleted = false;
                state.SuppressNextDetection = true;
                state.DesiredEnabled = matchingEntry.IsEnabled;
                state.ObservedEnabled = matchingEntry.IsEnabled;
                state.BackupFilePath = null;
                state.DeletedAtUtc = null;
                state.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await _stateStore.SaveAsync(states, cancellationToken);
            }

            return;
        }

        var removed = false;
        foreach (var state in states.Values
                     .Where(state => IsMatchingEnhanceMenuState(state, relativeGroupPath, itemElement))
                     .ToList())
        {
            state.IsPendingApproval = false;
            state.SuppressNextDetection = false;
            state.IsDeleted = false;
            state.BackupFilePath = null;
            state.DeletedAtUtc = null;
            state.DesiredEnabled = null;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (matchingEntry is null)
            {
                states.Remove(state.Id);
                removed = true;
            }
        }

        if (!removed && matchingEntry is not null)
        {
            var state = GetOrCreateState(states, matchingEntry);
            state.IsPendingApproval = false;
            state.SuppressNextDetection = false;
            state.DesiredEnabled = matchingEntry.IsEnabled;
            state.ObservedEnabled = matchingEntry.IsEnabled;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await _stateStore.SaveAsync(states, cancellationToken);
    }

    private static ContextMenuEntry? FindEnhanceMenuEntry(
        IEnumerable<ContextMenuEntry> snapshot,
        string relativeGroupPath,
        XElement itemElement)
    {
        if (itemElement.Attribute("KeyName") is not null)
        {
            var keyName = itemElement.Attribute("KeyName")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return null;
            }

            return snapshot.FirstOrDefault(entry =>
                entry.EntryKind == ContextMenuEntryKind.ShellVerb
                && string.Equals(entry.SourceRootPath, $@"{relativeGroupPath}\shell", StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.KeyName, keyName, StringComparison.OrdinalIgnoreCase));
        }

        var guidText = itemElement.Element("Guid")?.Value?.Trim();
        if (!Guid.TryParse(guidText, out var guid))
        {
            return null;
        }

        return snapshot.FirstOrDefault(entry =>
            entry.EntryKind == ContextMenuEntryKind.ShellExtension
            && string.Equals(entry.SourceRootPath, $@"{relativeGroupPath}\shellex\ContextMenuHandlers", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(entry.HandlerClsid, out var handlerGuid)
            && handlerGuid == guid);
    }

    private static bool IsMatchingEnhanceMenuState(
        PersistedContextMenuState state,
        string relativeGroupPath,
        XElement itemElement)
    {
        if (itemElement.Attribute("KeyName") is not null)
        {
            var keyName = itemElement.Attribute("KeyName")?.Value?.Trim();
            return state.EntryKind == ContextMenuEntryKind.ShellVerb
                   && string.Equals(state.SourceRootPath, $@"{relativeGroupPath}\shell", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(state.KeyName, keyName, StringComparison.OrdinalIgnoreCase);
        }

        var guidText = itemElement.Element("Guid")?.Value?.Trim();
        return state.EntryKind == ContextMenuEntryKind.ShellExtension
               && string.Equals(state.SourceRootPath, $@"{relativeGroupPath}\shellex\ContextMenuHandlers", StringComparison.OrdinalIgnoreCase)
               && Guid.TryParse(guidText, out var expectedGuid)
               && Guid.TryParse(state.HandlerClsid, out var stateGuid)
               && expectedGuid == stateGuid;
    }

    /// <summary>
    /// Gets registry Protection Setting Async.
    /// </summary>
    public async Task<PipeResponse> GetRegistryProtectionSettingAsync(CancellationToken cancellationToken)
    {
        var settings = await _protectionSettingsStore.LoadAsync(cancellationToken);
        return new PipeResponse
        {
            Success = true,
            Message = "Registry protection setting loaded.",
            RegistryProtectionEnabled = settings.LockNewContextMenuItems
        };
    }

    /// <summary>
    /// Sets registry Protection Setting Async.
    /// </summary>
    public async Task<PipeResponse> SetRegistryProtectionSettingAsync(bool enable, CancellationToken cancellationToken)
    {
        var errors = ApplyRegistryWriteProtection(enable);
        if (errors.Count > 0)
        {
            var detail = string.Join(Environment.NewLine, errors);
            await _logger.LogAsync(RuntimeLogLevel.Warning, $"Registry write protection update skipped some protected roots:{Environment.NewLine}{detail}", cancellationToken);
        }

        var settings = await _protectionSettingsStore.LoadAsync(cancellationToken);
        settings.LockNewContextMenuItems = enable;
        await _protectionSettingsStore.SaveAsync(settings, cancellationToken);
        await _logger.LogAsync($"Registry write protection for new context menu items changed to {enable}.", cancellationToken);

        return new PipeResponse
        {
            Success = true,
            Message = errors.Count == 0
                ? "Registry protection setting updated."
                : $"Registry protection setting updated. Some protected system roots were skipped.{Environment.NewLine}{string.Join(Environment.NewLine, errors)}",
            RegistryProtectionEnabled = enable
        };
    }

    /// <summary>
    /// Deletes item Async.
    /// </summary>
    public async Task<PipeResponse> DeleteItemAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        var persistedState = states.GetValueOrDefault(itemId);
        var usedFallbackItem = false;
        ContextMenuEntry? item = null;

        if (item is null)
        {
            var snapshot = await GetSnapshotAsync(cancellationToken);
            item = SelectPreferredDeleteCandidate(
                snapshot.Where(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase)),
                currentUserSid: null);
        }

        if (item is null)
        {
            item = await TryFindEntryByIdAsync(itemId, cancellationToken);
        }

        if (item is null && persistedState is null)
        {
            return CreateFailure($"Menu item '{itemId}' was not found.");
        }

        if (item is not null && item.IsDeleted)
        {
            return CreateFailure($"Menu item '{item.DisplayName}' is already deleted.", item);
        }

        if (item is null && persistedState?.IsDeleted == true)
        {
            var deletedItem = CreateVirtualEntry(persistedState, GetDeletedConsistencyIssue(persistedState), ContextMenuChangeKind.None, null);
            return CreateFailure($"Menu item '{persistedState.DisplayName}' is already deleted.", deletedItem);
        }

        if (item is not null && !item.IsPresentInRegistry)
        {
            return await RemoveMissingItemStateAsync(item, cancellationToken);
        }

        try
        {
            var backendRegistryPath = item?.BackendRegistryPath ?? persistedState?.BackendRegistryPath;
            if (string.IsNullOrWhiteSpace(backendRegistryPath))
            {
                return CreateFailure($"Cannot delete '{itemId}': registry path is unknown.");
            }

            using (var existingKey = OpenRegistryKey(backendRegistryPath, writable: false))
            {
                var targetExists = existingKey is not null;
                await _logger.LogAsync(
                    "Preparing to delete context menu item: "
                    + $"itemId={itemId}; "
                    + $"displayName={item?.DisplayName ?? persistedState?.DisplayName ?? itemId}; "
                    + $"registryPath={item?.RegistryPath ?? persistedState?.RegistryPath}; "
                    + $"backendRegistryPath={backendRegistryPath}; "
                    + $"handlerClsid={item?.HandlerClsid ?? persistedState?.HandlerClsid}; "
                    + $"usedFallbackItem={usedFallbackItem}; "
                    + $"targetExists={targetExists}.",
                    cancellationToken);

                if (!targetExists)
                {
                    return await RemoveStaleDeleteStateAsync(
                        itemId,
                        item?.DisplayName ?? persistedState?.DisplayName ?? itemId,
                        states,
                        cancellationToken);
                }
            }

            string backupFilePath;
            try
            {
                await _logger.LogAsync($"DeleteItemBackupStart: ItemId={itemId}, RegistryPath={backendRegistryPath}.", cancellationToken);
                backupFilePath = await _backupService.ExportKeyAsync(backendRegistryPath, cancellationToken);
                await _logger.LogAsync($"DeleteItemBackupEnd: ItemId={itemId}, RegistryPath={backendRegistryPath}, BackupPath={backupFilePath}, Result=Success.", cancellationToken);
            }
            catch (InvalidOperationException ex) when (IsRegistryKeyMissingExportError(ex.Message))
            {
                return await RemoveStaleDeleteStateAsync(
                    itemId,
                    item?.DisplayName ?? persistedState?.DisplayName ?? itemId,
                    states,
                    cancellationToken);
            }

            await _logger.LogAsync($"DeleteRegistryKeyStart: ItemId={itemId}, BackendRegistryPath={backendRegistryPath}.", cancellationToken);
            DeleteRegistryKey(backendRegistryPath);
            await _logger.LogAsync($"DeleteRegistryKeyEnd: ItemId={itemId}, BackendRegistryPath={backendRegistryPath}, Result=Success.", cancellationToken);

            var state = GetOrCreateState(states, item ?? CreateMinimalEntry(itemId, persistedState!));
            state.DesiredEnabled = null;
            state.IsDeleted = true;
            state.IsPendingApproval = false;
            state.BackupFilePath = backupFilePath;
            state.DeletedAtUtc = DateTimeOffset.UtcNow;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _stateStore.SaveAsync(states, cancellationToken);
            await _logger.LogAsync($"DeleteItemStateSaved: ItemId={itemId}, BackupPath={backupFilePath}, IsDeleted=true.", cancellationToken);
            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"ShellChangeNotifierCalled: Action=DeleteItem, ItemId={itemId}.", cancellationToken);

            await _logger.LogAsync($"Deleted {state.DisplayName} with backup {backupFilePath}.", cancellationToken);

            var refreshed = SelectPreferredDeleteCandidate(
                    (await GetSnapshotAsync(cancellationToken))
                    .Where(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase)),
                    currentUserSid: null)
                ?? CreateVirtualEntry(state, null, ContextMenuChangeKind.None, null);

            return new PipeResponse
            {
                Success = true,
                Message = $"Deleted {state.DisplayName}.",
                Item = refreshed
            };
        }
        catch (Exception ex)
        {
            var displayName = item?.DisplayName ?? persistedState?.DisplayName ?? itemId;
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to delete {displayName}: {ex}", cancellationToken);
            return CreateFailure(ex.Message, item);
        }
    }

    private async Task<PipeResponse> RemoveMissingItemStateAsync(ContextMenuEntry item, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        if (states.Remove(item.Id))
        {
            PruneTransientStates(states);
            await _stateStore.SaveAsync(states, cancellationToken);
        }

        await _logger.LogAsync($"Removed missing item {item.DisplayName} from the catalog state.", cancellationToken);

        return new PipeResponse
        {
            Success = true,
            Message = $"Removed missing item {item.DisplayName} from the list.",
            Item = null
        };
    }

    private async Task<PipeResponse> RemoveStaleDeleteStateAsync(
        string itemId,
        string displayName,
        Dictionary<string, PersistedContextMenuState> states,
        CancellationToken cancellationToken)
    {
        if (states.Remove(itemId))
        {
            PruneTransientStates(states);
            await _stateStore.SaveAsync(states, cancellationToken);
        }

        await _logger.LogAsync($"Delete skipped for missing registry key. Removed stale state for {displayName}.", cancellationToken);

        return new PipeResponse
        {
            Success = true,
            Message = $"Removed missing item {displayName} from the list.",
            Item = null
        };
    }

    private async Task<PipeResponse> RemovePendingApprovalItemAsync(
        ContextMenuEntry? item,
        string itemId,
        CancellationToken cancellationToken)
    {
        if (item is not null && item.IsPresentInRegistry && !item.IsDeleted)
        {
            return await DeleteItemAsync(itemId, cancellationToken);
        }

        return await RemovePendingApprovalStateAsync(itemId, cancellationToken);
    }

    private async Task<PipeResponse> RemovePendingApprovalStateAsync(string itemId, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        if (!states.TryGetValue(itemId, out var state))
        {
            return new PipeResponse
            {
                Success = true,
                Message = $"Approval item '{itemId}' is no longer present."
            };
        }

        var displayName = state.DisplayName;
        var deletedEntry = state.ToDeletedEntry();
        var shouldRemoveState = !state.IsDeleted && string.IsNullOrWhiteSpace(state.BackupFilePath);

        if (shouldRemoveState)
        {
            states.Remove(itemId);
        }
        else
        {
            state.IsPendingApproval = false;
            state.SuppressNextDetection = false;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        PruneTransientStates(states);
        await _stateStore.SaveAsync(states, cancellationToken);
        await _logger.LogAsync($"Removed {displayName} from the approval queue.", cancellationToken);

        return new PipeResponse
        {
            Success = true,
            Message = $"Removed {displayName} from the approval queue.",
            Item = shouldRemoveState ? null : deletedEntry with { IsPendingApproval = false }
        };
    }

    /// <summary>
    /// Executes undo Delete Async.
    /// </summary>
    public async Task<PipeResponse> UndoDeleteAsync(string itemId, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        if (!states.TryGetValue(itemId, out var state) || !state.IsDeleted || string.IsNullOrWhiteSpace(state.BackupFilePath))
        {
            return CreateFailure($"No backup was found for '{itemId}'.");
        }

        try
        {
            await _logger.LogAsync($"UndoDeleteRestoreStart: ItemId={itemId}, BackupPath={state.BackupFilePath}.", cancellationToken);
            await _backupService.RestoreBackupAsync(state.BackupFilePath, cancellationToken);
            await _logger.LogAsync($"UndoDeleteRestoreEnd: ItemId={itemId}, BackupPath={state.BackupFilePath}, Result=Success.", cancellationToken);
            _backupService.DeleteBackupFile(state.BackupFilePath);

            state.IsDeleted = false;
            state.BackupFilePath = null;
            state.DeletedAtUtc = null;
            state.IsPendingApproval = false;
            state.SuppressNextDetection = true;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            state.DesiredEnabled = null;
            PruneTransientStates(states);
            await _stateStore.SaveAsync(states, cancellationToken);
            await _logger.LogAsync($"UndoDeleteStateSaved: ItemId={itemId}, IsDeleted=false.", cancellationToken);
            ShellChangeNotifier.NotifyAssociationsChanged();
            await _logger.LogAsync($"ShellChangeNotifierCalled: Action=UndoDelete, ItemId={itemId}.", cancellationToken);

            await _logger.LogAsync($"Restored deleted item {state.DisplayName}.", cancellationToken);

            var refreshed = (await GetSnapshotAsync(cancellationToken))
                .FirstOrDefault(entry => string.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase));

            return new PipeResponse
            {
                Success = refreshed is not null,
                Message = refreshed is not null
                    ? $"Restored {refreshed.DisplayName}."
                    : $"The backup for {state.DisplayName} was restored, but the item could not be re-read.",
                Item = refreshed
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to restore {state.DisplayName}: {ex}", cancellationToken);
            return CreateFailure(ex.Message, state.ToDeletedEntry());
        }
    }

    /// <summary>
    /// Executes purge Deleted Item Async.
    /// </summary>
    public async Task<PipeResponse> PurgeDeletedItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        if (!states.TryGetValue(itemId, out var state) || !state.IsDeleted)
        {
            return CreateFailure($"Deleted item '{itemId}' was not found.");
        }

        try
        {
            await _logger.LogAsync($"PurgeDeletedBackupStart: ItemId={itemId}, BackupPath={state.BackupFilePath}.", cancellationToken);
            _backupService.DeleteBackupFile(state.BackupFilePath);
            await _logger.LogAsync($"PurgeDeletedBackupEnd: ItemId={itemId}, BackupPath={state.BackupFilePath}, Result=Success.", cancellationToken);
            states.Remove(itemId);
            await _stateStore.SaveAsync(states, cancellationToken);
            await _logger.LogAsync($"PurgeDeletedStateSaved: ItemId={itemId}, RemovedState=true.", cancellationToken);
            await _logger.LogAsync($"Permanently removed backup for {state.DisplayName}.", cancellationToken);

            return new PipeResponse
            {
                Success = true,
                Message = $"Permanently removed {state.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to permanently remove {state.DisplayName}: {ex}", cancellationToken);
            return CreateFailure(ex.Message, state.ToDeletedEntry());
        }
    }

    /// <summary>
    /// Executes mark Item Pending Approval Async.
    /// </summary>
    public async Task MarkItemPendingApprovalAsync(ContextMenuEntry item, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        var state = GetOrCreateState(states, item);
        state.IsPendingApproval = true;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(states, cancellationToken);
    }

    /// <summary>
    /// Executes quarantine New Item Async.
    /// </summary>
    public async Task<ContextMenuEntry> QuarantineNewItemAsync(ContextMenuEntry item, CancellationToken cancellationToken)
    {
        // Step 1: disable the newly detected item immediately. This keeps the
        // service in a deny-by-default posture until the user explicitly allows it.
        switch (item.EntryKind)
        {
            case ContextMenuEntryKind.ShellVerb when !item.IsWindows11ContextMenu:
                SetShellVerbEnabled(item.BackendRegistryPath, enable: false);
                break;
            case ContextMenuEntryKind.ShellExtension when item.IsWindows11ContextMenu:
                await _logger.LogAsync(
                    RuntimeLogLevel.Warning,
                    "Quarantining Win11 context menu entry without frontend user context; using compatibility fallback user detection.",
                    cancellationToken);
                if (!_windows11Catalog.SetEnabled(item.HandlerClsid ?? item.KeyName, item.DisplayName, null, enable: false))
                {
                    throw new InvalidOperationException($"Unable to quarantine the Win11 context menu item '{item.DisplayName}'.");
                }
                break;
            case ContextMenuEntryKind.ShellExtension:
                SetShellExtensionEnabled(item, enable: false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported entry kind: {item.EntryKind}");
        }

        var states = await _stateStore.LoadAsync(cancellationToken);
        var state = GetOrCreateState(states, item);

        // Step 2: persist the blocked state and mark it as waiting for approval.
        state.DesiredEnabled = false;
        state.ObservedEnabled = false;
        state.IsPendingApproval = true;
        state.IsDeleted = false;
        state.DeletedAtUtc = null;
        state.BackupFilePath = null;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(states, cancellationToken);
        ShellChangeNotifier.NotifyAssociationsChanged();

        await _logger.LogAsync($"Quarantined new menu item pending approval: {item.DisplayName} ({item.RegistryPath}).", cancellationToken);

        return (await GetSnapshotAsync(cancellationToken))
            .FirstOrDefault(entry => string.Equals(entry.Id, item.Id, StringComparison.OrdinalIgnoreCase))
            ?? item with
            {
                IsEnabled = false,
                IsPendingApproval = true
            };
    }

    /// <summary>
    /// Executes log Consistency Summary Async.
    /// </summary>
    public async Task<int> LogConsistencySummaryAsync(CancellationToken cancellationToken)
    {
        var inconsistencies = (await GetReadOnlySnapshotAsync(cancellationToken)).Count(static entry => entry.HasConsistencyIssue);
        await _logger.LogAsync($"Consistency check complete. Inconsistent items: {inconsistencies}.", cancellationToken);
        return inconsistencies;
    }

    /// <summary>
    /// Attempts to consume Suppressed Detection Async.
    /// </summary>
    public async Task<bool> TryConsumeSuppressedDetectionAsync(string itemId, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        if (!states.TryGetValue(itemId, out var state) || !state.SuppressNextDetection)
        {
            return false;
        }

        state.SuppressNextDetection = false;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(states, cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<ContextMenuEntry>> EnumerateActualEntriesAsync(CancellationToken cancellationToken)
    {
        await _logger.LogAsync($"EnumerateActualEntriesStart: MonitoredRootCount={MonitoredRoots.Length}, Win11Supported={_windows11Catalog.IsSupported}.", cancellationToken);
        var results = new List<ContextMenuEntry>();
        foreach (var item in EnumerateEntries(MonitoredRoots))
        {
            results.Add(item);
        }

        var classicCount = results.Count;
        var win11Count = 0;
        if (_windows11Catalog.IsSupported)
        {
            await _logger.LogAsync(
                RuntimeLogLevel.Warning,
                "Enumerating Win11 context menu entries without frontend user context; user-level blocked state may be incomplete in this compatibility snapshot path.",
                cancellationToken);
            var win11Entries = await _windows11Catalog.EnumerateEntriesAsync(cancellationToken);
            win11Count = win11Entries.Count;
            results.AddRange(win11Entries);
        }

        await _logger.LogAsync($"EnumerateActualEntriesEnd: MonitoredRootCount={MonitoredRoots.Length}, RegistryEntriesFound={classicCount}, Win11EntriesAdded={win11Count}, TotalEntries={results.Count}.", cancellationToken);
        return results;
    }

    private IEnumerable<ContextMenuEntry> EnumerateEntries(IEnumerable<RegistryRootDescriptor> roots, string? currentUserSid = null)
    {
        foreach (var root in roots)
        {
            foreach (var item in EnumerateRoot(root, currentUserSid))
            {
                yield return item;
            }
        }
    }

    private IEnumerable<ContextMenuEntry> EnumerateRoot(RegistryRootDescriptor root, string? currentUserSid = null)
    {
        foreach (var instance in EnumerateRootInstances(currentUserSid))
        {
            using var baseKey = instance.OpenBaseKey(root.RelativePath);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames().OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
            {
                using var itemKey = baseKey.OpenSubKey(subKeyName, writable: false);
                if (itemKey is null)
                {
                    continue;
                }

                var defaultValue = itemKey.GetValue(null)?.ToString();
                var handlerClsid = root.EntryKind == ContextMenuEntryKind.ShellExtension
                    ? ResolveShellExtensionHandlerClsid(subKeyName, defaultValue)
                    : null;
                var displayName = ResolveDisplayName(root, itemKey, subKeyName, defaultValue, handlerClsid);
                var editableText = root.EntryKind == ContextMenuEntryKind.ShellVerb
                    ? ResolveEditableText(itemKey, defaultValue)
                    : null;
                var commandText = root.EntryKind == ContextMenuEntryKind.ShellVerb
                    ? itemKey.OpenSubKey("command", writable: false)?.GetValue(null)?.ToString()
                    : null;

                var (iconPath, iconIndex) = root.EntryKind switch
                {
                    ContextMenuEntryKind.ShellVerb => ShellMetadataResolver.ResolveVerbIcon(itemKey, commandText),
                    ContextMenuEntryKind.ShellExtension => ShellMetadataResolver.ResolveShellExtensionIcon(handlerClsid),
                    _ => (null, 0)
                };
                var filePath = root.EntryKind switch
                {
                    ContextMenuEntryKind.ShellVerb => ShellMetadataResolver.ResolveVerbFilePath(itemKey, commandText),
                    ContextMenuEntryKind.ShellExtension => ShellMetadataResolver.ResolveShellExtensionFilePath(handlerClsid),
                    _ => null
                };

                iconPath = GuidMetadataCatalog.NormalizeCandidatePath(iconPath, filePath);

                var effectiveRelativePath = $@"{root.RelativePath}\{subKeyName}";
                var isEnabled = root.EntryKind switch
                {
                    ContextMenuEntryKind.ShellVerb => itemKey.GetValue("LegacyDisable") is null,
                    ContextMenuEntryKind.ShellExtension => !IsShellExtensionBlocked(handlerClsid),
                    _ => true
                };

                yield return new ContextMenuEntry
                {
                    Id = $"{root.StableRelativePath}|{subKeyName}",
                    Category = root.Category,
                    EntryKind = root.EntryKind,
                    KeyName = subKeyName,
                    DisplayName = displayName,
                    EditableText = editableText,
                    RegistryPath = effectiveRelativePath,
                    BackendRegistryPath = instance.ComposeAbsolutePath(effectiveRelativePath),
                    SourceRootPath = root.StableRelativePath,
                    CommandText = commandText,
                    HandlerClsid = handlerClsid,
                    IconPath = iconPath,
                    IconIndex = iconIndex,
                    FilePath = filePath,
                    OnlyWithShift = root.EntryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("Extended") is not null,
                    OnlyInExplorer = root.EntryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("OnlyInBrowserWindow") is not null,
                    NoWorkingDirectory = root.EntryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("NoWorkingDirectory") is not null,
                    NeverDefault = root.EntryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("NeverDefault") is not null,
                    ShowAsDisabledIfHidden = root.EntryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("ShowAsDisabledIfHidden") is not null,
                    IsEnabled = isEnabled,
                    IsPresentInRegistry = true,
                    Notes = BuildNotes(root.EntryKind, commandText, handlerClsid)
                };
            }
        }
    }

    private static string ResolveDisplayName(
        RegistryRootDescriptor root,
        RegistryKey itemKey,
        string fallbackKeyName,
        string? rawDefaultValue,
        string? handlerClsid)
    {
        var displayName = itemKey.Name.Contains(@"\shellex\", StringComparison.OrdinalIgnoreCase)
            ? ShellMetadataResolver.ResolveShellExtensionDisplayName(fallbackKeyName, handlerClsid)
            : ShellMetadataResolver.ResolveVerbDisplayName(itemKey, fallbackKeyName);

        if (itemKey.Name.Contains(@"\shellex\", StringComparison.OrdinalIgnoreCase)
            && string.Equals(displayName, fallbackKeyName, StringComparison.Ordinal)
            && Guid.TryParse(fallbackKeyName, out _)
            && !string.IsNullOrWhiteSpace(rawDefaultValue)
            && !Guid.TryParse(rawDefaultValue, out _))
        {
            displayName = rawDefaultValue;
        }

        if (root.Category == ContextMenuCategory.RecycleBin
            && root.RelativePath.EndsWith(@"\shellex\PropertySheetHandlers", StringComparison.OrdinalIgnoreCase))
        {
            displayName = "Properties";
        }

        return NormalizeDisplayName(displayName);
    }

    private static string? ResolveShellExtensionHandlerClsid(string keyName, string? defaultValue)
    {
        if (Guid.TryParse(defaultValue, out var defaultGuid))
        {
            return defaultGuid.ToString("B");
        }

        if (Guid.TryParse(keyName, out var keyGuid))
        {
            return keyGuid.ToString("B");
        }

        return string.IsNullOrWhiteSpace(defaultValue)
            ? null
            : defaultValue.Trim();
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        const string escapedAmpersandToken = "\uF000";
        var normalized = displayName
            .Replace("&&", escapedAmpersandToken, StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace(escapedAmpersandToken, "&", StringComparison.Ordinal)
            .Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? displayName.Trim()
            : normalized;
    }

    private static string? BuildNotes(ContextMenuEntryKind kind, string? commandText, string? handlerClsid)
    {
        return kind switch
        {
            ContextMenuEntryKind.ShellVerb when !string.IsNullOrWhiteSpace(commandText) => commandText,
            ContextMenuEntryKind.ShellExtension when !string.IsNullOrWhiteSpace(handlerClsid) => $"Handler CLSID: {handlerClsid}",
            _ => null
        };
    }

    private static string? GetConsistencyIssue(ContextMenuEntry entry, PersistedContextMenuState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (state.IsDeleted)
        {
            return "This item was deleted through the app, but it has reappeared in the registry.";
        }

        if (state.DesiredEnabled is { } desiredEnabled && entry.IsEnabled != desiredEnabled)
        {
            return $"Saved state expects this item to be {(desiredEnabled ? "enabled" : "disabled")}, but the registry currently reports {(entry.IsEnabled ? "enabled" : "disabled")}.";
        }

        return null;
    }

    private static ContextMenuChangeKind GetDetectedChangeKind(ContextMenuEntry entry, PersistedContextMenuState? state, bool hasBaseline)
    {
        if (state is null)
        {
            return hasBaseline ? ContextMenuChangeKind.Added : ContextMenuChangeKind.None;
        }

        if (state.IsDeleted)
        {
            return ContextMenuChangeKind.Reappeared;
        }

        return HasObservedChange(entry, state)
            ? ContextMenuChangeKind.Modified
            : ContextMenuChangeKind.None;
    }

    private static string? GetDetectedChangeDetails(ContextMenuEntry entry, PersistedContextMenuState? state, ContextMenuChangeKind changeKind)
    {
        return changeKind switch
        {
            ContextMenuChangeKind.Added => "This item is new compared with the last saved context menu snapshot.",
            ContextMenuChangeKind.Reappeared => "This item was previously deleted through the app, but it has reappeared in the registry.",
            ContextMenuChangeKind.Modified when state is not null => BuildModifiedDetails(entry, state),
            _ => null
        };
    }

    private static string? GetDeletedConsistencyIssue(PersistedContextMenuState state)
    {
        if (string.IsNullOrWhiteSpace(state.BackupFilePath) || !File.Exists(state.BackupFilePath))
        {
            return "The backup file for this deleted item is missing.";
        }

        return null;
    }

    private static bool UpdateMetadata(PersistedContextMenuState state, ContextMenuEntry entry)
    {
        var dirty = false;

        dirty |= UpdateIfChanged(state.Category, entry.Category, value => state.Category = value);
        dirty |= UpdateIfChanged(state.DisplayName, entry.DisplayName, value => state.DisplayName = value);
        dirty |= UpdateIfChanged(state.EditableText, entry.EditableText, value => state.EditableText = value);
        dirty |= UpdateIfChanged(state.RegistryPath, entry.RegistryPath, value => state.RegistryPath = value);
        dirty |= UpdateIfChanged(state.BackendRegistryPath, entry.BackendRegistryPath, value => state.BackendRegistryPath = value);
        dirty |= UpdateIfChanged(state.SourceRootPath, entry.SourceRootPath, value => state.SourceRootPath = value);
        dirty |= UpdateIfChanged(state.CommandText, entry.CommandText, value => state.CommandText = value);
        dirty |= UpdateIfChanged(state.HandlerClsid, entry.HandlerClsid, value => state.HandlerClsid = value);
        dirty |= UpdateIfChanged(state.IconPath, entry.IconPath, value => state.IconPath = value);
        dirty |= UpdateIfChanged(state.IconIndex, entry.IconIndex, value => state.IconIndex = value);
        dirty |= UpdateIfChanged(state.FilePath, entry.FilePath, value => state.FilePath = value);
        dirty |= UpdateIfChanged(state.IsWindows11ContextMenu, entry.IsWindows11ContextMenu, value => state.IsWindows11ContextMenu = value);
        dirty |= UpdateIfChanged(state.OnlyWithShift, entry.OnlyWithShift, value => state.OnlyWithShift = value);
        dirty |= UpdateIfChanged(state.OnlyInExplorer, entry.OnlyInExplorer, value => state.OnlyInExplorer = value);
        dirty |= UpdateIfChanged(state.NoWorkingDirectory, entry.NoWorkingDirectory, value => state.NoWorkingDirectory = value);
        dirty |= UpdateIfChanged(state.NeverDefault, entry.NeverDefault, value => state.NeverDefault = value);
        dirty |= UpdateIfChanged(state.ShowAsDisabledIfHidden, entry.ShowAsDisabledIfHidden, value => state.ShowAsDisabledIfHidden = value);
        dirty |= UpdateIfChanged(state.Notes, entry.Notes, value => state.Notes = value);
        dirty |= UpdateIfChanged(state.ObservedEnabled, entry.IsEnabled, value => state.ObservedEnabled = value);

        if (dirty)
        {
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        return dirty;
    }

    private static bool UpdateIfChanged<T>(T current, T updated, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, updated))
        {
            return false;
        }

        setter(updated);
        return true;
    }

    private static ContextMenuEntry CreateVirtualEntry(
        PersistedContextMenuState state,
        string? issue,
        ContextMenuChangeKind changeKind,
        string? changeDetails)
    {
        if (state.IsDeleted)
        {
            return state.ToDeletedEntry(issue);
        }

        return new ContextMenuEntry
        {
            Id = state.Id,
            Category = state.Category,
            EntryKind = state.EntryKind,
            KeyName = state.KeyName,
            DisplayName = state.DisplayName,
            EditableText = state.EditableText,
            RegistryPath = state.RegistryPath,
            BackendRegistryPath = state.BackendRegistryPath,
            SourceRootPath = state.SourceRootPath,
            CommandText = state.CommandText,
            HandlerClsid = state.HandlerClsid,
            IconPath = state.IconPath,
            IconIndex = state.IconIndex,
            FilePath = state.FilePath,
            IsWindows11ContextMenu = state.IsWindows11ContextMenu,
            OnlyWithShift = state.OnlyWithShift,
            OnlyInExplorer = state.OnlyInExplorer,
            NoWorkingDirectory = state.NoWorkingDirectory,
            NeverDefault = state.NeverDefault,
            ShowAsDisabledIfHidden = state.ShowAsDisabledIfHidden,
            IsPresentInRegistry = false,
            IsEnabled = state.DesiredEnabled ?? true,
            Notes = state.Notes,
            IsDeleted = false,
            IsPendingApproval = state.IsPendingApproval,
            HasBackup = !string.IsNullOrWhiteSpace(state.BackupFilePath),
            DeletedAtUtc = state.DeletedAtUtc,
            DetectedChangeKind = changeKind,
            DetectedChangeDetails = changeDetails,
            HasConsistencyIssue = !string.IsNullOrWhiteSpace(issue),
            ConsistencyIssue = issue
        };
    }

    private static ContextMenuEntry CreateMinimalEntry(string itemId, PersistedContextMenuState state)
    {
        return new ContextMenuEntry
        {
            Id = itemId,
            Category = state.Category,
            EntryKind = state.EntryKind,
            KeyName = state.KeyName,
            DisplayName = state.DisplayName,
            EditableText = state.EditableText,
            RegistryPath = state.RegistryPath,
            BackendRegistryPath = state.BackendRegistryPath,
            SourceRootPath = state.SourceRootPath,
            CommandText = state.CommandText,
            HandlerClsid = state.HandlerClsid,
            IconPath = state.IconPath,
            IconIndex = state.IconIndex,
            FilePath = state.FilePath,
            IsWindows11ContextMenu = state.IsWindows11ContextMenu,
            OnlyWithShift = state.OnlyWithShift,
            OnlyInExplorer = state.OnlyInExplorer,
            NoWorkingDirectory = state.NoWorkingDirectory,
            NeverDefault = state.NeverDefault,
            ShowAsDisabledIfHidden = state.ShowAsDisabledIfHidden,
            IsPresentInRegistry = true,
            IsEnabled = state.DesiredEnabled ?? true,
            Notes = state.Notes
        };
    }

    private async Task<ContextMenuEntry?> TryFindEntryByIdAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var separatorIndex = itemId.LastIndexOf('|');
        if (separatorIndex < 0)
        {
            return null;
        }

        var stableRelativePath = itemId[..separatorIndex];
        var keyName = itemId[(separatorIndex + 1)..];
        var candidates = new List<ContextMenuEntry>();

        foreach (var instance in EnumerateRootInstances())
        {
            using var baseKey = instance.OpenBaseKey(stableRelativePath);
            if (baseKey is null)
            {
                continue;
            }

            using var itemKey = baseKey.OpenSubKey(keyName, writable: false);
            if (itemKey is null)
            {
                continue;
            }

            var category = DetermineCategoryFromPath(stableRelativePath);
            var entryKind = stableRelativePath.Contains(@"\shellex\", StringComparison.OrdinalIgnoreCase)
                ? ContextMenuEntryKind.ShellExtension
                : ContextMenuEntryKind.ShellVerb;
            var defaultValue = itemKey.GetValue(null)?.ToString();
            var handlerClsid = entryKind == ContextMenuEntryKind.ShellExtension
                ? ResolveShellExtensionHandlerClsid(keyName, defaultValue)
                : null;
            var displayName = ResolveDisplayName(
                new RegistryRootDescriptor(category, stableRelativePath, entryKind),
                itemKey,
                keyName,
                defaultValue,
                handlerClsid);
            var editableText = entryKind == ContextMenuEntryKind.ShellVerb
                ? ResolveEditableText(itemKey, defaultValue)
                : null;
            var commandText = entryKind == ContextMenuEntryKind.ShellVerb
                ? itemKey.OpenSubKey("command", writable: false)?.GetValue(null)?.ToString()
                : null;

            var (iconPath, iconIndex) = entryKind switch
            {
                ContextMenuEntryKind.ShellVerb => ShellMetadataResolver.ResolveVerbIcon(itemKey, commandText),
                ContextMenuEntryKind.ShellExtension => ShellMetadataResolver.ResolveShellExtensionIcon(handlerClsid),
                _ => (null, 0)
            };

            var filePath = entryKind switch
            {
                ContextMenuEntryKind.ShellVerb => ShellMetadataResolver.ResolveVerbFilePath(itemKey, commandText),
                ContextMenuEntryKind.ShellExtension => ShellMetadataResolver.ResolveShellExtensionFilePath(handlerClsid),
                _ => null
            };

            iconPath = GuidMetadataCatalog.NormalizeCandidatePath(iconPath, filePath);

            var effectiveRelativePath = $@"{stableRelativePath}\{keyName}";
            var isEnabled = entryKind switch
            {
                ContextMenuEntryKind.ShellVerb => itemKey.GetValue("LegacyDisable") is null,
                ContextMenuEntryKind.ShellExtension => !IsShellExtensionBlocked(handlerClsid),
                _ => true
            };

            candidates.Add(new ContextMenuEntry
            {
                Id = itemId,
                Category = category,
                EntryKind = entryKind,
                KeyName = keyName,
                DisplayName = displayName,
                EditableText = editableText,
                RegistryPath = effectiveRelativePath,
                BackendRegistryPath = instance.ComposeAbsolutePath(effectiveRelativePath),
                SourceRootPath = stableRelativePath,
                CommandText = commandText,
                HandlerClsid = handlerClsid,
                IconPath = iconPath,
                IconIndex = iconIndex,
                FilePath = filePath,
                OnlyWithShift = entryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("Extended") is not null,
                OnlyInExplorer = entryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("OnlyInBrowserWindow") is not null,
                NoWorkingDirectory = entryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("NoWorkingDirectory") is not null,
                NeverDefault = entryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("NeverDefault") is not null,
                ShowAsDisabledIfHidden = entryKind == ContextMenuEntryKind.ShellVerb && itemKey.GetValue("ShowAsDisabledIfHidden") is not null,
                IsEnabled = isEnabled,
                IsPresentInRegistry = true,
                Notes = BuildNotes(entryKind, commandText, handlerClsid)
            });
        }

        await Task.CompletedTask;
        return SelectPreferredDeleteCandidate(candidates, currentUserSid: null);
    }

    private static ContextMenuCategory DetermineCategoryFromPath(string stableRelativePath)
    {
        if (stableRelativePath.StartsWith(@"AllFilesystemObjects", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.AllFileSystemObjects;
        if (stableRelativePath.StartsWith(@"*\shell", StringComparison.OrdinalIgnoreCase) || stableRelativePath.StartsWith(@"*\shellex", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.File;
        if (stableRelativePath.StartsWith(@"Folder\shell", StringComparison.OrdinalIgnoreCase) || stableRelativePath.StartsWith(@"Folder\shellex", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.Folder;
        if (stableRelativePath.StartsWith(@"Directory\shell", StringComparison.OrdinalIgnoreCase) || stableRelativePath.StartsWith(@"Directory\shellex", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.Directory;
        if (stableRelativePath.StartsWith(@"Directory\Background", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.DirectoryBackground;
        if (stableRelativePath.StartsWith(@"DesktopBackground", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.DesktopBackground;
        if (stableRelativePath.StartsWith(@"Drive", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.Drive;
        if (stableRelativePath.StartsWith(@"LibraryFolder", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.Library;
        if (stableRelativePath.StartsWith(@"UserLibraryFolder", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.Library;
        if (stableRelativePath.StartsWith(@"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.Computer;
        if (stableRelativePath.StartsWith(@"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.RecycleBin;
        if (stableRelativePath.StartsWith(@"SystemFileAssociations", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.File;
        if (stableRelativePath.StartsWith(@"Launcher.ImmersiveApplication", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.File;
        if (stableRelativePath.StartsWith(@"lnkfile", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.File;
        if (stableRelativePath.StartsWith(@"exefile", StringComparison.OrdinalIgnoreCase))
            return ContextMenuCategory.File;

        return ContextMenuCategory.File;
    }

    private static ContextMenuEntry? SelectPreferredDeleteCandidate(
        IEnumerable<ContextMenuEntry> candidates,
        string? currentUserSid)
    {
        return candidates
            .Where(static entry => entry.IsPresentInRegistry && !entry.IsDeleted)
            .OrderBy(entry => GetDeleteTargetRank(entry, currentUserSid))
            .ThenBy(static entry => IsDisabledContainerEntry(entry) ? 1 : 0)
            .FirstOrDefault();
    }

    private static int GetDeleteTargetRank(ContextMenuEntry entry, string? currentUserSid)
    {
        if (!string.IsNullOrWhiteSpace(currentUserSid)
            && IsUnderUserClasses(entry.BackendRegistryPath, currentUserSid))
        {
            return 0;
        }

        if (entry.BackendRegistryPath.StartsWith(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\", StringComparison.OrdinalIgnoreCase)
            || entry.BackendRegistryPath.StartsWith(@"HKLM\SOFTWARE\Classes\", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static bool IsUnderUserClasses(string registryPath, string userSid)
    {
        return registryPath.StartsWith($@"HKEY_USERS\{userSid}\Software\Classes\", StringComparison.OrdinalIgnoreCase)
               || registryPath.StartsWith($@"HKU\{userSid}\Software\Classes\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRegistryKeyMissingExportError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("系统找不到指定的注册表项或值", StringComparison.Ordinal)
               || message.Contains("The system was unable to find the specified registry key or value", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unable to find the specified registry key", StringComparison.OrdinalIgnoreCase);
    }

    private static PersistedContextMenuState GetOrCreateState(
        IDictionary<string, PersistedContextMenuState> states,
        ContextMenuEntry entry)
    {
        if (states.TryGetValue(entry.Id, out var existing))
        {
            UpdateMetadata(existing, entry);
            return existing;
        }

        var state = PersistedContextMenuState.FromEntry(entry);
        states[entry.Id] = state;
        return state;
    }

    private static ContextMenuEntry? TryUseSceneFallbackItem(string itemId, ContextMenuEntry? fallbackItem)
    {
        if (fallbackItem is null
            || !string.Equals(fallbackItem.Id, itemId, StringComparison.OrdinalIgnoreCase)
            || fallbackItem.IsDeleted
            || !fallbackItem.IsPresentInRegistry
            || string.IsNullOrWhiteSpace(fallbackItem.BackendRegistryPath)
            || string.IsNullOrWhiteSpace(fallbackItem.RegistryPath)
            || string.IsNullOrWhiteSpace(fallbackItem.SourceRootPath))
        {
            return null;
        }

        if (fallbackItem.EntryKind is not ContextMenuEntryKind.ShellVerb
            and not ContextMenuEntryKind.ShellExtension)
        {
            return null;
        }

        using var itemKey = OpenRegistryKey(fallbackItem.BackendRegistryPath, writable: false);
        if (itemKey is null)
        {
            return null;
        }

        if (fallbackItem.EntryKind == ContextMenuEntryKind.ShellExtension)
        {
            if (string.IsNullOrWhiteSpace(fallbackItem.HandlerClsid))
            {
                return null;
            }

            var actualHandlerClsid = ResolveShellExtensionHandlerClsid(
                fallbackItem.KeyName,
                itemKey.GetValue(null)?.ToString());

            if (!string.Equals(actualHandlerClsid, fallbackItem.HandlerClsid, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return fallbackItem;
    }

    private static IReadOnlyList<ContextMenuEntry> GetStateLinkedEntries(
        IReadOnlyList<ContextMenuEntry> snapshot,
        ContextMenuEntry item)
    {
        if (item.EntryKind != ContextMenuEntryKind.ShellExtension || string.IsNullOrWhiteSpace(item.HandlerClsid))
        {
            return [item];
        }

        return snapshot
            .Where(entry =>
                entry.EntryKind == ContextMenuEntryKind.ShellExtension
                && string.Equals(entry.HandlerClsid, item.HandlerClsid, StringComparison.OrdinalIgnoreCase))
            .Append(item)
            .GroupBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static void UpdateLinkedShellExtensionPersistedStates(
        IDictionary<string, PersistedContextMenuState> states,
        string? handlerClsid,
        bool enable)
    {
        if (string.IsNullOrWhiteSpace(handlerClsid))
        {
            return;
        }

        foreach (var state in states.Values.Where(state =>
                     state.EntryKind == ContextMenuEntryKind.ShellExtension
                     && !state.IsDeleted
                     && string.Equals(state.HandlerClsid, handlerClsid, StringComparison.OrdinalIgnoreCase)))
        {
            state.DesiredEnabled = enable;
            state.ObservedEnabled = enable;
            state.IsPendingApproval = false;
            state.SuppressNextDetection = false;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private static void PruneTransientStates(IDictionary<string, PersistedContextMenuState> states)
    {
        foreach (var staleId in states
                     .Where(static pair => ShouldPruneTransientState(pair.Value))
                     .Select(static pair => pair.Key)
                     .ToList())
        {
            states.Remove(staleId);
        }
    }

    private static bool ShouldPruneTransientState(PersistedContextMenuState state)
    {
        // Keep the long-lived monitoring baseline for the real monitored roots,
        // but drop neutral leftovers from scene pages or previous buggy versions
        // so they do not pollute future startup comparisons.
        if (MonitoredStableRootPaths.Contains(state.SourceRootPath))
        {
            return false;
        }

        if (state.IsWindows11ContextMenu
            || string.Equals(state.SourceRootPath, Windows11MonitoredRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !state.IsDeleted
               && !state.IsPendingApproval
               && !state.SuppressNextDetection
               && state.DesiredEnabled is null
               && string.IsNullOrWhiteSpace(state.BackupFilePath);
    }

    private static bool HasObservedChange(ContextMenuEntry entry, PersistedContextMenuState state)
    {
        return HasExternalEnabledStateChange(entry, state)
               || state.Category != entry.Category
               || !string.Equals(state.DisplayName, entry.DisplayName, StringComparison.Ordinal)
               || !string.Equals(state.EditableText, entry.EditableText, StringComparison.Ordinal)
               || !string.Equals(state.CommandText, entry.CommandText, StringComparison.Ordinal)
               || !string.Equals(state.HandlerClsid, entry.HandlerClsid, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(state.IconPath, entry.IconPath, StringComparison.OrdinalIgnoreCase)
               || state.IconIndex != entry.IconIndex
               || !string.Equals(state.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)
               || state.IsWindows11ContextMenu != entry.IsWindows11ContextMenu
               || state.OnlyWithShift != entry.OnlyWithShift
               || state.OnlyInExplorer != entry.OnlyInExplorer
               || state.NoWorkingDirectory != entry.NoWorkingDirectory
               || state.NeverDefault != entry.NeverDefault
               || state.ShowAsDisabledIfHidden != entry.ShowAsDisabledIfHidden
               || !string.Equals(state.Notes, entry.Notes, StringComparison.Ordinal)
               || !string.Equals(state.SourceRootPath, entry.SourceRootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildModifiedDetails(ContextMenuEntry entry, PersistedContextMenuState state)
    {
        var changedParts = new List<string>();
        if (HasExternalEnabledStateChange(entry, state))
        {
            changedParts.Add("enabled state");
        }

        if (!string.Equals(state.DisplayName, entry.DisplayName, StringComparison.Ordinal))
        {
            changedParts.Add("display name");
        }

        if (!string.Equals(state.EditableText, entry.EditableText, StringComparison.Ordinal))
        {
            changedParts.Add("menu text");
        }

        if (!string.Equals(state.CommandText, entry.CommandText, StringComparison.Ordinal))
        {
            changedParts.Add("command");
        }

        if (!string.Equals(state.HandlerClsid, entry.HandlerClsid, StringComparison.OrdinalIgnoreCase))
        {
            changedParts.Add("handler GUID");
        }

        if (!string.Equals(state.IconPath, entry.IconPath, StringComparison.OrdinalIgnoreCase)
            || state.IconIndex != entry.IconIndex)
        {
            changedParts.Add("icon");
        }

        if (!string.Equals(state.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            changedParts.Add("module path");
        }

        if (state.OnlyWithShift != entry.OnlyWithShift)
        {
            changedParts.Add("extended visibility");
        }

        if (state.OnlyInExplorer != entry.OnlyInExplorer)
        {
            changedParts.Add("explorer-only flag");
        }

        if (state.NoWorkingDirectory != entry.NoWorkingDirectory)
        {
            changedParts.Add("working directory flag");
        }

        if (state.NeverDefault != entry.NeverDefault)
        {
            changedParts.Add("default action flag");
        }

        if (state.ShowAsDisabledIfHidden != entry.ShowAsDisabledIfHidden)
        {
            changedParts.Add("show-disabled-when-hidden flag");
        }

        if (!string.Equals(state.Notes, entry.Notes, StringComparison.Ordinal))
        {
            changedParts.Add("details");
        }

        if (!string.Equals(state.SourceRootPath, entry.SourceRootPath, StringComparison.OrdinalIgnoreCase)
            || state.Category != entry.Category)
        {
            changedParts.Add("category");
        }

        return changedParts.Count == 0
            ? "This item changed outside the app."
            : $"This item changed outside the app. Updated fields: {string.Join(", ", changedParts)}.";
    }

    private static bool HasExternalEnabledStateChange(ContextMenuEntry entry, PersistedContextMenuState state)
    {
        if (state.DesiredEnabled is { } desiredEnabled)
        {
            return entry.IsEnabled != desiredEnabled;
        }

        return false;
    }

    private static bool IsShellExtensionBlocked(string? handlerClsid)
    {
        if (string.IsNullOrWhiteSpace(handlerClsid))
        {
            return false;
        }

        using var blockedKey = Registry.LocalMachine.OpenSubKey(BlockedShellExtensionsPath, writable: false);
        return blockedKey?.GetValue(handlerClsid) is not null;
    }

    private void SetShellVerbEnabled(string registryPath, bool enable)
    {
        using var menuKey = OpenRegistryKey(registryPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open {registryPath} for writing.");

        if (enable)
        {
            menuKey.DeleteValue("LegacyDisable", throwOnMissingValue: false);
            _logger.LogFireAndForget(
                DiagnosticLogFormatter.BuildRegistryOperationLog(
                    "SetShellVerbEnabled",
                    registryPath,
                    "LegacyDisable",
                    RegistryValueKind.String,
                    null,
                    writable: true,
                    result: "DeleteValue Success, Enable=true"));
        }
        else
        {
            menuKey.SetValue("LegacyDisable", string.Empty, RegistryValueKind.String);
            _logger.LogFireAndForget(
                DiagnosticLogFormatter.BuildRegistryOperationLog(
                    "SetShellVerbEnabled",
                    registryPath,
                    "LegacyDisable",
                    RegistryValueKind.String,
                    string.Empty,
                    writable: true,
                    result: "SetValue Success, Enable=false"));
        }
    }

    private void SetShellVerbAttribute(string registryPath, ContextMenuShellAttribute attribute, bool enable)
    {
        using var menuKey = OpenRegistryKey(registryPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open {registryPath} for writing.");

        var valueName = attribute switch
        {
            ContextMenuShellAttribute.OnlyWithShift => "Extended",
            ContextMenuShellAttribute.OnlyInExplorer => "OnlyInBrowserWindow",
            ContextMenuShellAttribute.NoWorkingDirectory => "NoWorkingDirectory",
            ContextMenuShellAttribute.NeverDefault => "NeverDefault",
            ContextMenuShellAttribute.ShowAsDisabledIfHidden => "ShowAsDisabledIfHidden",
            _ => throw new InvalidOperationException($"Unsupported shell attribute: {attribute}")
        };

        if (enable)
        {
            menuKey.SetValue(valueName, string.Empty, RegistryValueKind.String);
            _logger.LogFireAndForget(DiagnosticLogFormatter.BuildRegistryOperationLog($"SetShellVerbAttribute:{attribute}", registryPath, valueName, RegistryValueKind.String, string.Empty, writable: true, result: "SetValue Success"));
        }
        else
        {
            menuKey.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.LogFireAndForget(DiagnosticLogFormatter.BuildRegistryOperationLog($"SetShellVerbAttribute:{attribute}", registryPath, valueName, RegistryValueKind.String, null, writable: true, result: "DeleteValue Success"));
        }
    }

    private void SetShellExtensionEnabled(ContextMenuEntry item, bool enable)
    {
        if (string.IsNullOrWhiteSpace(item.HandlerClsid))
        {
            throw new InvalidOperationException("Shell extension entries require a CLSID to be enabled or disabled.");
        }

        using var blockedKey = Registry.LocalMachine.CreateSubKey(BlockedShellExtensionsPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open {BlockedShellExtensionsPath} for writing.");

        if (enable)
        {
            blockedKey.DeleteValue(item.HandlerClsid, throwOnMissingValue: false);
            _logger.LogFireAndForget(DiagnosticLogFormatter.BuildRegistryOperationLog("SetShellExtensionEnabled", @"HKEY_LOCAL_MACHINE\" + BlockedShellExtensionsPath, item.HandlerClsid, RegistryValueKind.String, null, writable: true, result: "DeleteValue Success, Enable=true"));
        }
        else
        {
            blockedKey.SetValue(item.HandlerClsid, item.DisplayName, RegistryValueKind.String);
            _logger.LogFireAndForget(DiagnosticLogFormatter.BuildRegistryOperationLog("SetShellExtensionEnabled", @"HKEY_LOCAL_MACHINE\" + BlockedShellExtensionsPath, item.HandlerClsid, RegistryValueKind.String, item.DisplayName, writable: true, result: "SetValue Success, Enable=false"));
        }
    }

    private static void DeleteRegistryKey(string registryPath)
    {
        DeleteRegistryKeyTree(registryPath);
    }

    private static void SetEnhanceShellItemEnabled(
        string relativeGroupPath,
        XElement itemElement,
        bool enable,
        string cultureName)
    {
        var keyName = itemElement.Attribute("KeyName")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(keyName))
        {
            throw new InvalidOperationException("Enhance shell items require a KeyName attribute.");
        }

        var registryPath = $@"{relativeGroupPath}\shell\{keyName}";
        if (enable)
        {
            WriteEnhanceSubKeysValue(itemElement, registryPath, cultureName);
        }
        else
        {
            DeleteRegistrySubKeyTreeWithFallback(registryPath);
        }
    }

    private static void SetEnhanceShellExItemEnabled(string relativeGroupPath, XElement itemElement, bool enable)
    {
        var guidText = itemElement.Element("Guid")?.Value?.Trim();
        if (!Guid.TryParse(guidText, out var guid))
        {
            throw new InvalidOperationException("Enhance shell extension items require a valid Guid element.");
        }

        var keyName = itemElement.Element("KeyName")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(keyName))
        {
            keyName = guid.ToString("B");
        }

        var handlersPath = $@"{relativeGroupPath}\shellex\ContextMenuHandlers";
        if (enable)
        {
            EnableBackupPrivilege();
            EnableRestorePrivilege();

            RegistryKey? handlersKey;
            try
            {
                handlersKey = Registry.ClassesRoot.CreateSubKey(handlersPath, writable: true);
            }
            catch (UnauthorizedAccessException)
            {
                var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                handlersKey = machineRoot?.CreateSubKey(handlersPath, writable: true);
                if (handlersKey is null)
                {
                    throw new InvalidOperationException($"Unable to open {handlersPath} for writing.");
                }
            }

            using (handlersKey)
            {
                foreach (var subKeyName in handlersKey.GetSubKeyNames())
                {
                    using var subKey = handlersKey.OpenSubKey(subKeyName, writable: false);
                    var value = subKey?.GetValue(null)?.ToString();
                    if (Guid.TryParse(value, out var actualGuid) && actualGuid == guid)
                    {
                        return;
                    }
                }
            }

            var targetPath = $@"{handlersPath}\{keyName}";
            using var targetKey = Registry.ClassesRoot.OpenSubKey(targetPath, writable: false);
            var targetValue = targetKey?.GetValue(null)?.ToString();
            if (targetKey is not null
                && (!Guid.TryParse(targetValue, out var existingGuid) || existingGuid != guid))
            {
                targetPath = GetUniqueRegistryPath(handlersPath, keyName);
            }

            try
            {
                Registry.SetValue($@"HKEY_CLASSES_ROOT\{targetPath}", string.Empty, guid.ToString("B"), RegistryValueKind.String);
            }
            catch (UnauthorizedAccessException)
            {
                using var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                using var targetKeyWritable = machineRoot?.CreateSubKey(targetPath, writable: true);
                targetKeyWritable?.SetValue(string.Empty, guid.ToString("B"), RegistryValueKind.String);
            }
        }
        else
        {
            EnableBackupPrivilege();
            EnableRestorePrivilege();

            using var handlersKey = Registry.ClassesRoot.OpenSubKey(handlersPath, writable: true);
            if (handlersKey is null)
            {
                return;
            }

            foreach (var subKeyName in handlersKey.GetSubKeyNames())
            {
                using var subKey = handlersKey.OpenSubKey(subKeyName, writable: false);
                var value = subKey?.GetValue(null)?.ToString();
                if (Guid.TryParse(value, out var actualGuid) && actualGuid == guid)
                {
                    try
                    {
                        handlersKey.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        DeleteRegistrySubKeyTreeWithFallback($@"{handlersPath}\{subKeyName}");
                    }
                }
            }
        }
    }

    private static void WriteEnhanceSubKeysValue(XElement keyElement, string registryPath, string cultureName)
    {
        if (!ShouldIncludeNode(keyElement, cultureName))
        {
            return;
        }

        EnableBackupPrivilege();
        EnableRestorePrivilege();

        var defaultValue = keyElement.Attribute("Default")?.Value;
        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            try
            {
                Registry.SetValue($@"HKEY_CLASSES_ROOT\{registryPath}", string.Empty, Environment.ExpandEnvironmentVariables(defaultValue));
            }
            catch (UnauthorizedAccessException)
            {
                using var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                using var key = machineRoot?.CreateSubKey(registryPath, writable: true);
                key?.SetValue(string.Empty, Environment.ExpandEnvironmentVariables(defaultValue), RegistryValueKind.String);
            }
        }
        else if (string.Equals(keyElement.Name.LocalName, "Command", StringComparison.OrdinalIgnoreCase))
        {
            WriteEnhanceCommandValue(keyElement, registryPath, cultureName);
        }

        WriteEnhanceAttributesValue(keyElement.Element("Value"), registryPath, cultureName);

        var subKeyElement = keyElement.Element("SubKey");
        if (subKeyElement is null)
        {
            return;
        }

        foreach (var childElement in subKeyElement.Elements())
        {
            WriteEnhanceSubKeysValue(childElement, $@"{registryPath}\{childElement.Name.LocalName}", cultureName);
        }
    }

    private static void WriteEnhanceAttributesValue(XElement? valueElement, string registryPath, string cultureName)
    {
        if (valueElement is null || !ShouldIncludeNode(valueElement, cultureName))
        {
            return;
        }

        EnableBackupPrivilege();
        EnableRestorePrivilege();

        RegistryKey? key;
        try
        {
            key = Registry.ClassesRoot.CreateSubKey(registryPath, writable: true);
        }
        catch (UnauthorizedAccessException)
        {
            var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
            key = machineRoot?.CreateSubKey(registryPath, writable: true);
            if (key is null)
            {
                throw new InvalidOperationException($"Unable to open {registryPath} for writing.");
            }
        }

        using (key)
        {
            foreach (var valueNode in valueElement.Elements().Where(element => ShouldIncludeNode(element, cultureName)))
            {
                foreach (var attribute in valueNode.Attributes())
                {
                    if (string.IsNullOrWhiteSpace(attribute.Name.LocalName)
                        || string.Equals(attribute.Name.LocalName, "Default", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var attributeValue = attribute.Value;
                    try
                    {
                        switch (valueNode.Name.LocalName)
                        {
                            case "REG_SZ":
                                key.SetValue(attribute.Name.LocalName, Environment.ExpandEnvironmentVariables(attributeValue), RegistryValueKind.String);
                                break;
                            case "REG_EXPAND_SZ":
                                key.SetValue(attribute.Name.LocalName, attributeValue, RegistryValueKind.ExpandString);
                                break;
                            case "REG_BINARY":
                                key.SetValue(attribute.Name.LocalName, ConvertToBinary(attributeValue), RegistryValueKind.Binary);
                                break;
                            case "REG_DWORD":
                                var numericBase = attributeValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10;
                                var numericValue = numericBase == 16 ? attributeValue[2..] : attributeValue;
                                key.SetValue(attribute.Name.LocalName, Convert.ToInt32(numericValue, numericBase), RegistryValueKind.DWord);
                                break;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        using var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                        using var machineKey = machineRoot?.CreateSubKey(registryPath, writable: true);
                        if (machineKey is not null)
                        {
                            switch (valueNode.Name.LocalName)
                            {
                                case "REG_SZ":
                                    machineKey.SetValue(attribute.Name.LocalName, Environment.ExpandEnvironmentVariables(attributeValue), RegistryValueKind.String);
                                    break;
                                case "REG_EXPAND_SZ":
                                    machineKey.SetValue(attribute.Name.LocalName, attributeValue, RegistryValueKind.ExpandString);
                                    break;
                                case "REG_BINARY":
                                    machineKey.SetValue(attribute.Name.LocalName, ConvertToBinary(attributeValue), RegistryValueKind.Binary);
                                    break;
                                case "REG_DWORD":
                                    var numericBase = attributeValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10;
                                    var numericValue = numericBase == 16 ? attributeValue[2..] : attributeValue;
                                    machineKey.SetValue(attribute.Name.LocalName, Convert.ToInt32(numericValue, numericBase), RegistryValueKind.DWord);
                                    break;
                            }
                        }
                    }
                }
            }
        }
    }

    private static void WriteEnhanceCommandValue(XElement commandElement, string registryPath, string cultureName)
    {
        var fileNameElement = commandElement.Element("FileName");
        var argumentsElement = commandElement.Element("Arguments");
        var shellExecuteElement = commandElement.Element("ShellExecute");

        var fileName = fileNameElement?.Value?.Trim();
        var arguments = argumentsElement?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = CreateEnhanceCommandFile(fileNameElement, cultureName);
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            arguments = CreateEnhanceCommandFile(argumentsElement, cultureName);
        }

        fileName = Environment.ExpandEnvironmentVariables(fileName ?? string.Empty);
        arguments = Environment.ExpandEnvironmentVariables(arguments ?? string.Empty);
        arguments = $"{argumentsElement?.Attribute("Prefix")?.Value}{arguments}{argumentsElement?.Attribute("Suffix")?.Value}";

        string command;
        if (shellExecuteElement is not null)
        {
            var verb = shellExecuteElement.Attribute("Verb")?.Value ?? "open";
            var windowStyle = int.TryParse(shellExecuteElement.Attribute("WindowStyle")?.Value, out var parsedStyle) ? parsedStyle : 1;
            var directory = shellExecuteElement.Attribute("Directory")?.Value;
            command = BuildShellExecuteCommand(fileName, arguments, verb, windowStyle, directory);
        }
        else
        {
            command = fileName;
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                command += $" {arguments}";
            }
        }

        EnableBackupPrivilege();
        EnableRestorePrivilege();

        try
        {
            Registry.SetValue($@"HKEY_CLASSES_ROOT\{registryPath}", string.Empty, command, RegistryValueKind.String);
        }
        catch (UnauthorizedAccessException)
        {
            using var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
            using var key = machineRoot?.CreateSubKey(registryPath, writable: true);
            key?.SetValue(string.Empty, command, RegistryValueKind.String);
        }
    }

    private static string CreateEnhanceCommandFile(XElement? parentElement, string cultureName)
    {
        if (parentElement is null)
        {
            return string.Empty;
        }

        var generatedDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ContextMenuMgr",
            "Programs");
        Directory.CreateDirectory(generatedDir);

        foreach (var createFileElement in parentElement.Elements("CreateFile").Where(element => ShouldIncludeNode(element, cultureName)))
        {
            var fileName = createFileElement.Attribute("FileName")?.Value;
            var content = createFileElement.Attribute("Content")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var safeFileName = SanitizeEnhanceProgramFileName(fileName);
            var filePath = Path.Combine(generatedDir, safeFileName);
            var encoding = string.Equals(Path.GetExtension(fileName), ".bat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(fileName), ".cmd", StringComparison.OrdinalIgnoreCase)
                    ? Encoding.Default
                    : Encoding.Unicode;

            File.WriteAllText(filePath, content, encoding);
            return filePath;
        }

        return string.Empty;
    }

    private static string BuildShellExecuteCommand(
        string fileName,
        string arguments,
        string verb,
        int windowStyle,
        string? directory)
    {
        arguments = arguments.Replace("\"", "\"\"");
        directory = string.IsNullOrWhiteSpace(directory)
            ? Path.GetDirectoryName(ExtractExecutablePath(fileName))
            : Environment.ExpandEnvironmentVariables(directory);

        return "mshta vbscript:createobject(\"shell.application\").shellexecute"
            + $"(\"{fileName}\",\"{arguments}\",\"{directory}\",\"{verb}\",{windowStyle})(close)";
    }

    private static string SanitizeEnhanceProgramFileName(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException("CreateFile requires a valid file name.");
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            safeFileName = safeFileName.Replace(invalidChar, '_');
        }

        if (safeFileName is "." or "..")
        {
            throw new InvalidOperationException("CreateFile file name cannot be a relative path segment.");
        }

        return safeFileName;
    }

    private static string ExtractExecutablePath(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        var trimmed = rawValue.Trim();
        if (File.Exists(trimmed))
        {
            return trimmed;
        }

        foreach (var extension in new[] { ".exe", ".cmd", ".bat", ".dll", ".msc", ".cpl", ".ocx", ".ps1", ".vbs", ".js", ".hta" })
        {
            var index = trimmed.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var candidate = trimmed[..(index + extension.Length)];
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return trimmed;
    }

    private static byte[] ConvertToBinary(string value)
    {
        var compact = Regex.Replace(value, @"\s+", string.Empty);
        if (compact.Length == 0)
        {
            return [];
        }

        if (compact.Length % 2 != 0 || !Regex.IsMatch(compact, @"\A[0-9a-fA-F]+\z"))
        {
            throw new FormatException($"REG_BINARY value '{value}' is not valid hexadecimal byte data.");
        }

        var bytes = new byte[compact.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = Convert.ToByte(compact.Substring(index * 2, 2), 16);
        }

        return bytes;
    }

    private static bool ShouldIncludeNode(XElement element, string cultureName)
    {
        if (!HasRequiredFiles(element))
        {
            return false;
        }

        if (!MatchesOsVersion(element))
        {
            return false;
        }

        return MatchesCulture(element, cultureName);
    }

    private static bool HasRequiredFiles(XElement element)
    {
        foreach (var fileElement in element.Elements("FileExists"))
        {
            var candidate = Environment.ExpandEnvironmentVariables(fileElement.Value.Trim());
            if (!File.Exists(candidate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesCulture(XElement element, string cultureName)
    {
        var culture = element.Element("Culture")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(culture))
        {
            return true;
        }

        return string.Equals(culture, cultureName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOsVersion(XElement element)
    {
        foreach (var versionElement in element.Elements("OSVersion"))
        {
            if (!Version.TryParse(versionElement.Value.Trim(), out var version))
            {
                continue;
            }

            var compare = versionElement.Attribute("Compare")?.Value?.Trim() ?? ">=";
            var current = Environment.OSVersion.Version.CompareTo(version);
            var matched = compare switch
            {
                ">" => current > 0,
                "<" => current < 0,
                "=" => current == 0,
                ">=" => current >= 0,
                "<=" => current <= 0,
                _ => true
            };

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeEnhanceCultureName(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return NormalizeSupportedCultureName(CultureInfo.CurrentUICulture);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName.Trim());
            return NormalizeSupportedCultureName(culture);
        }
        catch (CultureNotFoundException)
        {
            return NormalizeSupportedCultureName(CultureInfo.CurrentUICulture);
        }
    }

    private static string NormalizeSupportedCultureName(CultureInfo culture)
    {
        if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(culture.Name, "zh-TW", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(culture.Name, "zh-HK", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(culture.Name, "zh-MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW"
                : "zh-CN";
        }

        return "en-US";
    }

    private static string GetUniqueRegistryPath(string basePath, string keyName)
    {
        var candidate = $@"{basePath}\{keyName}";
        if (Registry.ClassesRoot.OpenSubKey(candidate, writable: false) is null)
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            var indexedCandidate = $@"{basePath}\{keyName} ({index})";
            if (Registry.ClassesRoot.OpenSubKey(indexedCandidate, writable: false) is null)
            {
                return indexedCandidate;
            }
        }

        throw new InvalidOperationException($"Unable to allocate a unique registry key name for {keyName}.");
    }

    private static string? ResolveEditableText(RegistryKey itemKey, string? defaultValue)
    {
        var muiVerb = itemKey.GetValue("MUIVerb")?.ToString();
        if (!string.IsNullOrWhiteSpace(muiVerb))
        {
            return ShellMetadataResolver.ResolveResourceString(muiVerb);
        }

        if (!HasMultiItemSubCommands(itemKey) && !string.IsNullOrWhiteSpace(defaultValue))
        {
            return ShellMetadataResolver.ResolveResourceString(defaultValue);
        }

        return null;
    }

    private static bool HasMultiItemSubCommands(RegistryKey itemKey)
    {
        var subCommands = itemKey.GetValue("SubCommands")?.ToString();
        if (!string.IsNullOrWhiteSpace(subCommands))
        {
            return true;
        }

        var extendedSubCommandsKey = itemKey.GetValue("ExtendedSubCommandsKey")?.ToString();
        return !string.IsNullOrWhiteSpace(extendedSubCommandsKey);
    }

    private static bool CanEditDisplayText(ContextMenuEntry item)
    {
        if (item.EntryKind != ContextMenuEntryKind.ShellVerb)
        {
            return false;
        }

        return !HasMultiItemSubCommands(item.RegistryPath)
               && !string.Equals(item.KeyName, "open", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMultiItemSubCommands(string registryPath)
    {
        using var itemKey = Registry.ClassesRoot.OpenSubKey(registryPath, writable: false);
        return itemKey is not null && HasMultiItemSubCommands(itemKey);
    }

    private static PipeResponse CreateFailure(string message, ContextMenuEntry? item = null)
    {
        return new PipeResponse
        {
            Success = false,
            Message = message,
            Item = item
        };
    }

    private List<string> ApplyRegistryWriteProtection(bool enable)
    {
        var errors = new List<string>();

        foreach (var relativePath in MonitoredRoots
                     .Select(static root => root.StableRelativePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ApplyRegistryWriteProtection(RegistryHive.LocalMachine, relativePath, enable, errors);

        }

        return errors;
    }

    private static void ApplyRegistryWriteProtectionToUserKey(RegistryKey userRoot, string relativePath, bool enable, List<string> errors)
    {
        try
        {
            using var classesRoot = userRoot.OpenSubKey(@"Software\Classes", writable: false);
            if (classesRoot is null)
            {
                return;
            }

            using var key = classesRoot.OpenSubKey(
                relativePath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ChangePermissions | RegistryRights.ReadKey);

            if (key is null)
            {
                return;
            }

            var security = key.GetAccessControl(AccessControlSections.Access);
            foreach (var rule in CreateProtectionRules())
            {
                if (enable)
                {
                    security.AddAccessRule(rule);
                }
                else
                {
                    security.RemoveAccessRuleSpecific(rule);
                }
            }

            key.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add($"Access denied to {relativePath} in user registry: {ex.Message}");
        }
        catch (SecurityException ex)
        {
            errors.Add($"Security error on {relativePath} in user registry: {ex.Message}");
        }
        catch (Exception ex)
        {
            errors.Add($"Error protecting {relativePath} in user registry: {ex.Message}");
        }
    }

    private void ApplyRegistryWriteProtection(RegistryHive hive, string relativePath, bool enable, List<string> errors)
    {
        var fullPath = $@"{hive}\Software\Classes\{relativePath}";
        var requestedRights = RegistryRights.ChangePermissions | RegistryRights.ReadKey;
        try
        {
            _logger.LogFireAndForget(DiagnosticLogFormatter.BuildAclOperationLog("RegistryWriteProtectionOpen", fullPath, requestedRights, $"Enable={enable}"));
            using var classesRoot = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = classesRoot.OpenSubKey(
                $@"Software\Classes\{relativePath}",
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                requestedRights);

            if (key is null)
            {
                _logger.LogFireAndForget(DiagnosticLogFormatter.BuildAclOperationLog("RegistryWriteProtectionSkipped", fullPath, requestedRights, "MissingKey"));
                return;
            }

            var security = key.GetAccessControl(AccessControlSections.Access);
            foreach (var rule in CreateProtectionRules())
            {
                _logger.LogFireAndForget($"RegistryWriteProtectionRule: Hive={hive}, Path={fullPath}, Enable={enable}, Rule={DiagnosticLogFormatter.FormatAclRule(rule)}.");
                if (enable)
                {
                    security.AddAccessRule(rule);
                }
                else
                {
                    security.RemoveAccessRuleSpecific(rule);
                }
            }

            key.SetAccessControl(security);
            _logger.LogFireAndForget(DiagnosticLogFormatter.BuildAclOperationLog("RegistryWriteProtectionSetAccessControl", fullPath, requestedRights, $"Success, Enable={enable}"));
        }
        catch (Exception ex)
        {
            _logger.LogFireAndForget(RuntimeLogLevel.Warning, $"RegistryWriteProtectionFailure: Hive={hive}, Path={fullPath}, Enable={enable}, Rights={DiagnosticLogFormatter.FormatRegistryRights(requestedRights)}, Exception={ex}");
            errors.Add($"{hive}\\Software\\Classes\\{relativePath}: {ex.Message}");
        }
    }

    private static IEnumerable<RegistryAccessRule> CreateProtectionRules()
    {
        var rights = RegistryRights.CreateSubKey | RegistryRights.SetValue;
        yield return new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            rights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        yield return new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            rights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        yield return new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            rights,
            InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Deny);

        yield return new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            rights,
            InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Deny);
    }

    private static IEnumerable<RegistryRootDescriptor> GetSceneRoots(ContextMenuSceneKind sceneKind, string? scopeValue)
    {
        return sceneKind switch
        {
            ContextMenuSceneKind.LnkFile => CreateShellSceneRoots(ContextMenuCategory.File, "lnkfile"),
            ContextMenuSceneKind.UwpShortcut => CreateShellSceneRoots(ContextMenuCategory.File, "Launcher.ImmersiveApplication"),
            ContextMenuSceneKind.ExeFile => CreateShellSceneRoots(ContextMenuCategory.File, "exefile"),
            ContextMenuSceneKind.UnknownType => CreateShellSceneRoots(ContextMenuCategory.File, "Unknown"),
            ContextMenuSceneKind.CustomExtension => CreateCustomExtensionRoots(scopeValue),
            ContextMenuSceneKind.PerceivedType => CreatePerceivedTypeRoots(scopeValue),
            ContextMenuSceneKind.DirectoryType => CreateDirectoryTypeRoots(scopeValue),
            ContextMenuSceneKind.CustomRegistryPath => CreateCustomRegistryPathRoots(scopeValue),
            _ => []
        };
    }

    private static IEnumerable<RegistryRootDescriptor> CreateShellSceneRoots(ContextMenuCategory category, string basePath)
    {
        yield return new RegistryRootDescriptor(category, $@"{basePath}\shell", ContextMenuEntryKind.ShellVerb);
        yield return new RegistryRootDescriptor(category, $@"{basePath}\shellex\ContextMenuHandlers", ContextMenuEntryKind.ShellExtension);
        yield return new RegistryRootDescriptor(
            category,
            $@"{basePath}\shellex\-ContextMenuHandlers",
            ContextMenuEntryKind.ShellExtension,
            $@"{basePath}\shellex\ContextMenuHandlers",
            true);
    }

    private static IEnumerable<RegistryRootDescriptor> CreateCustomExtensionRoots(string? scopeValue)
    {
        var extension = NormalizeExtension(scopeValue);
        if (string.IsNullOrWhiteSpace(extension))
        {
            yield break;
        }

        foreach (var root in CreateShellSceneRoots(ContextMenuCategory.File, $@"SystemFileAssociations\{extension}"))
        {
            yield return root;
        }

        foreach (var root in CreateShellSceneRoots(ContextMenuCategory.File, extension))
        {
            yield return root;
        }

        using var extensionKey = Registry.ClassesRoot.OpenSubKey(extension, writable: false);
        var progId = extensionKey?.GetValue(null)?.ToString();
        if (string.IsNullOrWhiteSpace(progId))
        {
            yield break;
        }

        foreach (var root in CreateShellSceneRoots(ContextMenuCategory.File, progId))
        {
            yield return root;
        }
    }

    private static IEnumerable<RegistryRootDescriptor> CreatePerceivedTypeRoots(string? scopeValue)
    {
        if (string.IsNullOrWhiteSpace(scopeValue))
        {
            yield break;
        }

        foreach (var root in CreateShellSceneRoots(ContextMenuCategory.File, $@"SystemFileAssociations\{scopeValue.Trim()}"))
        {
            yield return root;
        }
    }

    private static IEnumerable<RegistryRootDescriptor> CreateDirectoryTypeRoots(string? scopeValue)
    {
        if (string.IsNullOrWhiteSpace(scopeValue))
        {
            yield break;
        }

        foreach (var root in CreateShellSceneRoots(ContextMenuCategory.Directory, $@"SystemFileAssociations\Directory.{scopeValue.Trim()}"))
        {
            yield return root;
        }
    }

    private static IEnumerable<RegistryRootDescriptor> CreateCustomRegistryPathRoots(string? scopeValue)
    {
        var relativePath = NormalizeClassesRootRelativePath(scopeValue);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            yield break;
        }

        if (relativePath.EndsWith(@"\shell", StringComparison.OrdinalIgnoreCase))
        {
            yield return new RegistryRootDescriptor(ContextMenuCategory.File, relativePath, ContextMenuEntryKind.ShellVerb);
            yield break;
        }

        if (relativePath.EndsWith(@"\ContextMenuHandlers", StringComparison.OrdinalIgnoreCase))
        {
            yield return new RegistryRootDescriptor(ContextMenuCategory.File, relativePath, ContextMenuEntryKind.ShellExtension);
            yield break;
        }

        if (relativePath.EndsWith(@"\-ContextMenuHandlers", StringComparison.OrdinalIgnoreCase))
        {
            yield return new RegistryRootDescriptor(
                ContextMenuCategory.File,
                relativePath,
                ContextMenuEntryKind.ShellExtension,
                relativePath.Replace(@"\-ContextMenuHandlers", @"\ContextMenuHandlers", StringComparison.OrdinalIgnoreCase),
                true);
            yield break;
        }

        foreach (var root in CreateShellSceneRoots(ContextMenuCategory.File, relativePath))
        {
            yield return root;
        }
    }

    private static string? NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var extension = value.Trim();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return extension;
    }

    private static string? NormalizeClassesRootRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.Trim()
            .Replace('/', '\\')
            .Trim('\\');

        const string longPrefix = @"HKEY_CLASSES_ROOT\";
        const string shortPrefix = @"HKCR\";

        if (path.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[longPrefix.Length..];
        }
        else if (path.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[shortPrefix.Length..];
        }

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static ContextMenuEntry SelectPreferredActualEntry(ContextMenuEntry existing, ContextMenuEntry candidate)
    {
        var existingIsDisabledContainer = IsDisabledContainerEntry(existing);
        var candidateIsDisabledContainer = IsDisabledContainerEntry(candidate);

        if (existingIsDisabledContainer == candidateIsDisabledContainer)
        {
            return candidate;
        }

        return existingIsDisabledContainer ? candidate : existing;
    }

    private static bool IsDisabledContainerEntry(ContextMenuEntry entry)
    {
        return entry.RegistryPath.Contains(@"\-ContextMenuHandlers\", StringComparison.OrdinalIgnoreCase)
               || entry.BackendRegistryPath.Contains(@"\-ContextMenuHandlers\", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<RegistryRootInstance> EnumerateRootInstances(string? currentUserSid = null)
    {
        if (!string.IsNullOrWhiteSpace(currentUserSid))
        {
            // This order is intentional. BuildSnapshotAsync stores entries by logical
            // item id and later duplicates replace earlier ones, so current-user
            // classic registrations must be yielded last to win over duplicate HKLM
            // or other loaded HKU registrations.
            foreach (var userSid in EnumerateLoadedUserClassSids()
                         .Where(sid => !string.Equals(sid, currentUserSid, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new RegistryRootInstance(
                    Registry.Users,
                    $@"{userSid}\Software\Classes",
                    $@"HKEY_USERS\{userSid}\Software\Classes");
            }

            yield return new RegistryRootInstance(
                Registry.LocalMachine,
                @"SOFTWARE\Classes",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes");

            using var currentUserClassesKey = Registry.Users.OpenSubKey($@"{currentUserSid}\Software\Classes", writable: false);
            if (currentUserClassesKey is not null)
            {
                yield return new RegistryRootInstance(
                    Registry.Users,
                    $@"{currentUserSid}\Software\Classes",
                    $@"HKEY_USERS\{currentUserSid}\Software\Classes");
            }

            yield break;
        }

        yield return new RegistryRootInstance(
            Registry.LocalMachine,
            @"SOFTWARE\Classes",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes");

        foreach (var userSid in EnumerateLoadedUserClassSids())
        {
            yield return new RegistryRootInstance(
                Registry.Users,
                $@"{userSid}\Software\Classes",
                $@"HKEY_USERS\{userSid}\Software\Classes");
        }
    }

    private static IEnumerable<string> EnumerateLoadedUserClassSids()
    {
        return Registry.Users.GetSubKeyNames()
            .Where(static sid => sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
                                 && !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static sid => sid, StringComparer.OrdinalIgnoreCase);
    }

    private void WriteDetailedEditRegistryValue(
        string fullPath,
        string keyName,
        string? valueKind,
        string? value,
        string? userSid)
    {
        var kind = ParseDetailedEditRegistryValueKind(valueKind);
        var (baseKey, subPath) = OpenDetailedEditRegistryBaseKey(fullPath, userSid);
        using var key = baseKey.CreateSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open {fullPath} for writing.");

        if (value is null)
        {
            key.DeleteValue(keyName, throwOnMissingValue: false);
            _logger.LogFireAndForget(
                DiagnosticLogFormatter.BuildRegistryOperationLog(
                    "WriteDetailedEditRegistryValue",
                    fullPath,
                    keyName,
                    kind,
                    null,
                    writable: true,
                    result: $"DeleteValue Success, NormalizedSubPath={subPath}, UserSid={userSid ?? "<null>"}"));
            return;
        }

        object boxedValue = kind switch
        {
            RegistryValueKind.DWord => int.Parse(value, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(value, CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => ConvertToBinary(value),
            RegistryValueKind.MultiString => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => value
        };

        key.SetValue(keyName, boxedValue, kind);
        _logger.LogFireAndForget(
            DiagnosticLogFormatter.BuildRegistryOperationLog(
                "WriteDetailedEditRegistryValue",
                fullPath,
                keyName,
                kind,
                boxedValue,
                writable: true,
                result: $"SetValue Success, NormalizedSubPath={subPath}, UserSid={userSid ?? "<null>"}"));
    }

    private static RegistryValueKind ParseDetailedEditRegistryValueKind(string? valueKind)
    {
        if (string.IsNullOrWhiteSpace(valueKind))
        {
            return RegistryValueKind.String;
        }

        return Enum.TryParse<RegistryValueKind>(valueKind, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported registry value kind: {valueKind}");
    }

    private static (RegistryKey BaseKey, string SubPath) OpenDetailedEditRegistryBaseKey(
        string fullPath,
        string? userSid)
    {
        var normalized = fullPath.Replace('/', '\\').Trim();
        var separatorIndex = normalized.IndexOf('\\');
        var root = separatorIndex >= 0 ? normalized[..separatorIndex] : normalized;
        var subPath = separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : string.Empty;

        return root.ToUpperInvariant() switch
        {
            "HKEY_CLASSES_ROOT" or "HKCR" => (Registry.ClassesRoot, subPath),
            "HKEY_CURRENT_USER" or "HKCU" when !string.IsNullOrWhiteSpace(userSid)
                => (Registry.Users, $@"{userSid}\{subPath}"),
            "HKEY_CURRENT_USER" or "HKCU"
                => throw new InvalidOperationException("HKCU detailed edit writes require the frontend user SID."),
            "HKEY_LOCAL_MACHINE" or "HKLM" => (Registry.LocalMachine, subPath),
            "HKEY_USERS" or "HKU" => (Registry.Users, subPath),
            _ => throw new InvalidOperationException($"Unsupported registry root: {fullPath}")
        };
    }

    private static RegistryKey? OpenRegistryKey(string absoluteRegistryPath, bool writable)
    {
        if (TrySplitAbsoluteRegistryPath(absoluteRegistryPath, out var rootKey, out var subPath))
        {
            return rootKey.OpenSubKey(subPath, writable);
        }

        return Registry.ClassesRoot.OpenSubKey(absoluteRegistryPath, writable);
    }

    private static void DeleteRegistryKeyTree(string absoluteRegistryPath)
    {
        if (TrySplitAbsoluteRegistryPath(absoluteRegistryPath, out var rootKey, out var subPath))
        {
            rootKey.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            return;
        }

        Registry.ClassesRoot.DeleteSubKeyTree(absoluteRegistryPath, throwOnMissingSubKey: false);
    }

    private static bool TrySplitAbsoluteRegistryPath(string absoluteRegistryPath, out RegistryKey rootKey, out string subPath)
    {
        rootKey = null!;
        subPath = string.Empty;

        if (string.IsNullOrWhiteSpace(absoluteRegistryPath))
        {
            return false;
        }

        var normalized = absoluteRegistryPath.Trim();
        foreach (var pair in new (string Prefix, RegistryKey Key)[]
                 {
                     (@"HKEY_LOCAL_MACHINE\", Registry.LocalMachine),
                     (@"HKLM\", Registry.LocalMachine),
                     (@"HKEY_USERS\", Registry.Users),
                     (@"HKU\", Registry.Users),
                     (@"HKEY_CLASSES_ROOT\", Registry.ClassesRoot),
                     (@"HKCR\", Registry.ClassesRoot)
                 })
        {
            if (!normalized.StartsWith(pair.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rootKey = pair.Key;
            subPath = normalized[pair.Prefix.Length..];
            return true;
        }

        return false;
    }

    private sealed record RegistryRootDescriptor(
        ContextMenuCategory Category,
        string RelativePath,
        ContextMenuEntryKind EntryKind,
        string? StableRelativePath = null,
        bool IsDisabledContainer = false)
    {
        /// <summary>
        /// Gets the stable Relative Path.
        /// </summary>
        public string StableRelativePath { get; } = StableRelativePath ?? RelativePath;
    }

    private sealed record RegistryRootInstance(
        RegistryKey Hive,
        string ClassesBasePath,
        string AbsoluteRootPath)
    {
        /// <summary>
        /// Opens base Key.
        /// </summary>
        public RegistryKey? OpenBaseKey(string relativePath) => Hive.OpenSubKey($@"{ClassesBasePath}\{relativePath}", writable: false);

        /// <summary>
        /// Executes compose Absolute Path.
        /// </summary>
        public string ComposeAbsolutePath(string relativePath) => $@"{AbsoluteRootPath}\{relativePath}";
    }

    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_READ = 0x0008;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;
    private const string SE_RESTORE_NAME = "SeRestorePrivilege";
    private const string SE_BACKUP_NAME = "SeBackupPrivilege";
    private const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";

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

    private static bool EnableBackupPrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = System.Diagnostics.Process.GetCurrentProcess().SafeHandle;
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

    private static bool EnableRestorePrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = System.Diagnostics.Process.GetCurrentProcess().SafeHandle;
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

    private static bool EnableTakeOwnershipPrivilege()
    {
        try
        {
            IntPtr tokenHandle;
            var processHandle = System.Diagnostics.Process.GetCurrentProcess().SafeHandle;
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

    private static void DeleteRegistrySubKeyTreeWithFallback(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        EnableBackupPrivilege();
        EnableRestorePrivilege();

        try
        {
            DeleteRegistryKeyTree(fullPath);
            return;
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (TrySplitAbsoluteRegistryPath(fullPath, out var rootKey, out var subPath))
        {
            var parentPath = subPath.Contains('\\') ? subPath[..subPath.LastIndexOf('\\')] : string.Empty;
            var keyName = subPath.Contains('\\') ? subPath[(subPath.LastIndexOf('\\') + 1)..] : subPath;

            if (rootKey == Registry.ClassesRoot)
            {
                var machineRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true);
                if (machineRoot is not null)
                {
                    using var parent = machineRoot.OpenSubKey(parentPath, writable: true);
                    parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
                    return;
                }
            }
        }
    }
}

internal static class ShellChangeNotifier
{
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static void NotifyAssociationsChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
