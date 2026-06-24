using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Represents the context Menu State Store.
/// </summary>
public sealed class ContextMenuStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _storagePath;
    private readonly FileLogger? _logger;
    private readonly RuntimeHostIdentityProvider? _hostIdentityProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _isCurrentHostIdentityVerified = RuntimePaths.PackageKind != RuntimePackageKind.Portable;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenuStateStore"/> class.
    /// </summary>
    public ContextMenuStateStore(
        string storagePath,
        FileLogger? logger = null,
        RuntimeHostIdentityProvider? hostIdentityProvider = null)
    {
        _storagePath = storagePath;
        _logger = logger;
        _hostIdentityProvider = hostIdentityProvider;
        var directory = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public bool IsCurrentHostIdentityVerified => _isCurrentHostIdentityVerified;

    public event EventHandler<string>? PortableHostMismatchDetected;

    /// <summary>
    /// Loads async.
    /// </summary>
    public async Task<Dictionary<string, PersistedContextMenuState>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Executes save Async.
    /// </summary>
    public async Task SaveAsync(Dictionary<string, PersistedContextMenuState> states, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(states, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PersistedContextMenuState>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storagePath))
        {
            _logger?.LogFireAndForget($"ContextMenuStateStoreLoad: Path={_storagePath}, Exists=false, PersistedStateCount=0.");
            return new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase);
        }

        var json = await File.ReadAllBytesAsync(_storagePath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (TryReadEnvelope(document.RootElement, out var envelope))
        {
            return await LoadEnvelopeAsync(envelope, cancellationToken);
        }

        return await LoadLegacyDictionaryAsync(document.RootElement, cancellationToken);
    }

    private async Task SaveCoreAsync(Dictionary<string, PersistedContextMenuState> states, CancellationToken cancellationToken)
    {
        try
        {
            var identity = _hostIdentityProvider?.GetCurrent();
            if (RuntimePaths.PackageKind == RuntimePackageKind.Portable)
            {
                if (identity?.IsTrusted != true)
                {
                    _isCurrentHostIdentityVerified = false;
                    _logger?.LogFireAndForget(
                        RuntimeLogLevel.Warning,
                        $"ContextMenuStateStoreSaveSkipped: Path={_storagePath}, PackageKind=Portable, Reason=UntrustedHostIdentity, Detail={identity?.FailureReason ?? "MissingHostIdentityProvider"}.");
                    return;
                }

                await SaveEnvelopeAsync(states, identity, cancellationToken);
                _isCurrentHostIdentityVerified = true;
                return;
            }

            if (identity?.IsTrusted == true)
            {
                await SaveEnvelopeAsync(states, identity, cancellationToken);
                return;
            }

            await using var stream = File.Create(_storagePath);
            await JsonSerializer.SerializeAsync(stream, states, JsonOptions, cancellationToken);
            _logger?.LogFireAndForget($"ContextMenuStateStoreSave: Path={_storagePath}, PersistedStateCount={states.Count}, Result=Success.");
        }
        catch (Exception ex)
        {
            _logger?.LogFireAndForget(RuntimeLogLevel.Warning, $"ContextMenuStateStoreSaveFailed: Path={_storagePath}, PersistedStateCount={states.Count}, Exception={ex}");
            throw;
        }
    }

    private async Task<Dictionary<string, PersistedContextMenuState>> LoadEnvelopeAsync(
        ContextMenuStateEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var states = ToCaseInsensitiveDictionary(envelope.States);
        var storedPrefix = GetFingerprintPrefix(envelope.HostIdentity?.Fingerprint);

        if (RuntimePaths.PackageKind != RuntimePackageKind.Portable)
        {
            _isCurrentHostIdentityVerified = true;
            _logger?.LogFireAndForget(
                $"ContextMenuStateStoreLoad: Path={_storagePath}, Exists=true, Format=Envelope, StoredFingerprintPrefix={storedPrefix}, Action=loaded, PersistedStateCount={states.Count}.");
            return states;
        }

        var current = _hostIdentityProvider?.GetCurrent() ?? RuntimeHostIdentity.Untrusted("MissingHostIdentityProvider");
        if (!current.IsTrusted)
        {
            _isCurrentHostIdentityVerified = false;
            _logger?.LogFireAndForget(
                RuntimeLogLevel.Warning,
                $"PortableHostIdentityCheck: CurrentFingerprintPrefix=untrusted, StoredFingerprintPrefix={storedPrefix}, Action=reset, Reason=UntrustedCurrentIdentity.");
            return new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase);
        }

        if (string.Equals(current.Fingerprint, envelope.HostIdentity?.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            _isCurrentHostIdentityVerified = true;
            _logger?.LogFireAndForget(
                $"PortableHostIdentityCheck: CurrentFingerprintPrefix={current.FingerprintPrefix}, StoredFingerprintPrefix={storedPrefix}, Action=loaded, PersistedStateCount={states.Count}.");
            return states;
        }

        _isCurrentHostIdentityVerified = true;
        await QuarantineStateFileAsync(current.FingerprintPrefix, storedPrefix, cancellationToken);
        await SaveEnvelopeAsync(new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase), current, cancellationToken);
        _logger?.LogFireAndForget(
            RuntimeLogLevel.Warning,
            $"StateStoreHostMismatch: CurrentFingerprintPrefix={current.FingerprintPrefix}, StoredFingerprintPrefix={storedPrefix}, Action=quarantined.");
        PortableHostMismatchDetected?.Invoke(
            this,
            "Portable runtime state was created on another Windows installation or user profile, so ContextMenuMgr started with a fresh local state. The old state was moved to Quarantine.");
        return new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, PersistedContextMenuState>> LoadLegacyDictionaryAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var states = root.Deserialize<Dictionary<string, PersistedContextMenuState>>(JsonOptions);
        var result = ToCaseInsensitiveDictionary(states);

        if (RuntimePaths.PackageKind == RuntimePackageKind.Portable)
        {
            var current = _hostIdentityProvider?.GetCurrent() ?? RuntimeHostIdentity.Untrusted("MissingHostIdentityProvider");
            if (!current.IsTrusted)
            {
                _isCurrentHostIdentityVerified = false;
                _logger?.LogFireAndForget(
                    RuntimeLogLevel.Warning,
                    $"PortableHostIdentityCheck: CurrentFingerprintPrefix=untrusted, StoredFingerprintPrefix=<legacy>, Action=reset, Reason=LegacyStateUntrustedIdentity.");
                return new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase);
            }

            _isCurrentHostIdentityVerified = true;
            await SaveEnvelopeAsync(result, current, cancellationToken);
            _logger?.LogFireAndForget(
                $"PortableHostIdentityCheck: CurrentFingerprintPrefix={current.FingerprintPrefix}, StoredFingerprintPrefix=<legacy>, Action=migrated, PersistedStateCount={result.Count}.");
            return result;
        }

        var installerIdentity = _hostIdentityProvider?.GetCurrent();
        if (installerIdentity?.IsTrusted == true)
        {
            await SaveEnvelopeAsync(result, installerIdentity, cancellationToken);
            _logger?.LogFireAndForget(
                $"ContextMenuStateStoreLoad: Path={_storagePath}, Exists=true, Format=Legacy, Action=migrated, PersistedStateCount={result.Count}.");
        }
        else
        {
            _logger?.LogFireAndForget(
                $"ContextMenuStateStoreLoad: Path={_storagePath}, Exists=true, Format=Legacy, Action=loaded, PersistedStateCount={result.Count}.");
        }

        _isCurrentHostIdentityVerified = true;
        return result;
    }

    private async Task SaveEnvelopeAsync(
        Dictionary<string, PersistedContextMenuState> states,
        RuntimeHostIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_storagePath);
        await JsonSerializer.SerializeAsync(
            stream,
            new ContextMenuStateEnvelope
            {
                SchemaVersion = 2,
                HostIdentity = HostIdentityEnvelope.FromRuntimeIdentity(identity),
                States = states
            },
            JsonOptions,
            cancellationToken);
        _logger?.LogFireAndForget(
            $"ContextMenuStateStoreSave: Path={_storagePath}, SchemaVersion=2, HostFingerprintPrefix={identity.FingerprintPrefix}, PersistedStateCount={states.Count}, Result=Success.");
    }

    private async Task QuarantineStateFileAsync(
        string currentPrefix,
        string storedPrefix,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var targetDirectory = Path.Combine(
            RuntimePaths.QuarantineDirectory,
            $"foreign-host-{timestamp}-{storedPrefix}");
        var targetPath = Path.Combine(targetDirectory, Path.GetFileName(_storagePath));
        Directory.CreateDirectory(targetDirectory);
        targetPath = GetAvailablePath(targetPath);
        File.Move(_storagePath, targetPath, overwrite: false);
        await (_logger?.LogAsync(
            RuntimeLogLevel.Warning,
            $"StateStoreQuarantined: Source={_storagePath}, Target={targetPath}, CurrentFingerprintPrefix={currentPrefix}, StoredFingerprintPrefix={storedPrefix}.",
            cancellationToken) ?? Task.CompletedTask);
    }

    private static bool TryReadEnvelope(JsonElement root, out ContextMenuStateEnvelope envelope)
    {
        envelope = new ContextMenuStateEnvelope();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out _)
            || !root.TryGetProperty("states", out var statesElement)
            || statesElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        envelope = root.Deserialize<ContextMenuStateEnvelope>(JsonOptions) ?? new ContextMenuStateEnvelope();
        return true;
    }

    private static Dictionary<string, PersistedContextMenuState> ToCaseInsensitiveDictionary(
        Dictionary<string, PersistedContextMenuState>? states)
        => states is null
            ? new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PersistedContextMenuState>(states, StringComparer.OrdinalIgnoreCase);

    private static string GetFingerprintPrefix(string? fingerprint)
        => string.IsNullOrWhiteSpace(fingerprint)
            ? "<missing>"
            : fingerprint[..Math.Min(12, fingerprint.Length)];

    private static string GetAvailablePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName}-{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{fileName}-{Guid.NewGuid():N}{extension}");
    }

    private sealed class ContextMenuStateEnvelope
    {
        public int SchemaVersion { get; set; }

        public HostIdentityEnvelope? HostIdentity { get; set; }

        public Dictionary<string, PersistedContextMenuState>? States { get; set; }
    }

    private sealed class HostIdentityEnvelope
    {
        public string Kind { get; set; } = RuntimeHostIdentity.CurrentKind;

        public string Fingerprint { get; set; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int SchemaVersion { get; set; } = RuntimeHostIdentity.CurrentSchemaVersion;

        public static HostIdentityEnvelope FromRuntimeIdentity(RuntimeHostIdentity identity) => new()
        {
            Kind = identity.Kind,
            Fingerprint = identity.Fingerprint ?? string.Empty,
            CreatedAtUtc = identity.CreatedAtUtc,
            SchemaVersion = identity.SchemaVersion
        };
    }
}
