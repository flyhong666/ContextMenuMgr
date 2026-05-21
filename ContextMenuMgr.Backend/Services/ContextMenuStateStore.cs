using System.IO;
using System.Text.Json;
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
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenuStateStore"/> class.
    /// </summary>
    public ContextMenuStateStore(string storagePath, FileLogger? logger = null)
    {
        _storagePath = storagePath;
        _logger = logger;
        var directory = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

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

        await using var stream = File.OpenRead(_storagePath);
        var states = await JsonSerializer.DeserializeAsync<Dictionary<string, PersistedContextMenuState>>(stream, JsonOptions, cancellationToken);
        _logger?.LogFireAndForget($"ContextMenuStateStoreLoad: Path={_storagePath}, Exists=true, PersistedStateCount={states?.Count ?? 0}.");
        return states is null
            ? new Dictionary<string, PersistedContextMenuState>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PersistedContextMenuState>(states, StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveCoreAsync(Dictionary<string, PersistedContextMenuState> states, CancellationToken cancellationToken)
    {
        try
        {
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
}
