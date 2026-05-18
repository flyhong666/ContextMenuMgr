﻿using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Backend.Services;

// Named pipes keep the frontend unprivileged while still allowing duplex messaging
// for snapshot queries, toggle requests, and service-pushed notifications.
/// <summary>
/// Represents the named Pipe Backend Server.
/// </summary>
public sealed class NamedPipeBackendServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ContextMenuRegistryCatalog _catalog;
    private readonly SpecialMenuService _specialMenuService;
    private readonly Windows11BlocksService _windows11BlocksService;
    private readonly AutoStartService _autoStartService;
    private readonly FileTypeSceneMenuService _fileTypeSceneMenuService;
    private readonly ExplorerRestartService _explorerRestartService;
    private readonly FileLogger _logger;
    private readonly ConcurrentDictionary<Guid, PipeClientConnection> _clients = new();
    private CancellationTokenSource? _acceptLoopCts;
    private Task? _acceptLoopTask;

    public event EventHandler? BackendShutdownRequested;
    public event EventHandler? EnsureTrayHostRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeBackendServer"/> class.
    /// </summary>
    public NamedPipeBackendServer(
        ContextMenuRegistryCatalog catalog,
        SpecialMenuService specialMenuService,
        Windows11BlocksService windows11BlocksService,
        AutoStartService autoStartService,
        FileTypeSceneMenuService fileTypeSceneMenuService,
        ExplorerRestartService explorerRestartService,
        FileLogger logger)
    {
        _catalog = catalog;
        _specialMenuService = specialMenuService;
        _windows11BlocksService = windows11BlocksService;
        _autoStartService = autoStartService;
        _fileTypeSceneMenuService = fileTypeSceneMenuService;
        _explorerRestartService = explorerRestartService;
        _logger = logger;
    }

    /// <summary>
    /// Executes start.
    /// </summary>
    public void Start(CancellationToken cancellationToken)
    {
        _acceptLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_acceptLoopCts.Token), _acceptLoopCts.Token);
    }

    /// <summary>
    /// Executes stop.
    /// </summary>
    public void Stop()
    {
        _acceptLoopCts?.Cancel();
    }

    /// <summary>
    /// Executes broadcast Notification.
    /// </summary>
    public void BroadcastNotification(BackendNotification notification)
    {
        _ = BroadcastNotificationAsync(notification, CancellationToken.None);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServerStream();

                await server.WaitForConnectionAsync(cancellationToken);
                server.ReadMode = PipeTransmissionMode.Byte;
                _ = ObserveClientTaskAsync(server, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                await _logger.LogAsync(RuntimeLogLevel.Error, $"Named pipe accept loop failed: {ex.Message}", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private async Task ObserveClientTaskAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
        }
    }

    private static NamedPipeServerStream CreateServerStream()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        pipeSecurity.SetAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        pipeSecurity.SetAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
                    PipeConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    pipeSecurity);
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        var connection = new PipeClientConnection(stream);
        _clients[connection.Id] = connection;

        try
        {
            while (stream.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var line = await connection.Reader.ReadLineAsync().WaitAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var envelope = JsonSerializer.Deserialize<PipeEnvelope>(line, JsonOptions);
                if (envelope?.MessageType != PipeMessageType.Request || envelope.Request is null)
                {
                    await _logger.LogAsync(RuntimeLogLevel.Warning, $"Pipe payload from {connection.Id} was not a valid request.", cancellationToken);
                    continue;
                }

                if (envelope.Request.Command == PipeCommand.SubscribeNotifications)
                {
                    connection.IsNotificationSubscriber = true;
                    await _logger.LogAsync($"Connection {connection.Id} marked as frontend notification subscriber.", cancellationToken);
                }

                if (envelope.Request.Command == PipeCommand.SubscribeTrayHost)
                {
                    connection.IsNotificationSubscriber = true;
                    await _logger.LogAsync($"Connection {connection.Id} marked as tray-host subscriber.", cancellationToken);
                }

                PipeResponse response;
                try
                {
                    // Request handlers are allowed to fail independently; the pipe
                    // stays alive and the caller receives a structured error response.
                    response = await HandleRequestAsync(envelope.Request, stream, cancellationToken);
                }
                catch (Exception ex)
                {
                    await _logger.LogAsync(
                        RuntimeLogLevel.Error,
                        $"Pipe request {envelope.Request.Command} failed for {connection.Id}: {ex.Message}",
                        cancellationToken);
                    response = new PipeResponse
                    {
                        Success = false,
                        Message = ex.Message
                    };
                }

                await connection.SendAsync(
                    new PipeEnvelope
                    {
                        MessageType = PipeMessageType.Response,
                        CorrelationId = envelope.CorrelationId,
                        Response = response
                    },
                    cancellationToken);
                if (!response.Success)
                {
                    await _logger.LogAsync(
                        RuntimeLogLevel.Warning,
                        $"Pipe request {envelope.Request.Command} returned failure for {connection.Id}: {response.Message}",
                        cancellationToken);
                }

                if (response.Success && response.Item is not null)
                {
                    // Successful state-changing requests are rebroadcast so other
                    // connected surfaces can update without polling.
                    await BroadcastNotificationAsync(
                        new BackendNotification
                        {
                            Kind = PipeNotificationKind.ItemStateChanged,
                            Item = response.Item,
                            Message = response.Message,
                            ClientOperationId = response.ClientOperationId,
                            Timestamp = DateTimeOffset.UtcNow
                        },
                        cancellationToken);
                }

                if (response.Success && response.SpecialItem is not null)
                {
                    await BroadcastNotificationAsync(
                        new BackendNotification
                        {
                            Kind = PipeNotificationKind.ItemStateChanged,
                            SpecialKind = response.SpecialItem.Kind,
                            SpecialItem = response.SpecialItem,
                            Message = response.Message,
                            ClientOperationId = response.ClientOperationId,
                            Timestamp = DateTimeOffset.UtcNow
                        },
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Pipe client error: {ex.Message}", cancellationToken);
        }
        finally
        {
            var wasSubscriber = connection.IsNotificationSubscriber;
            _clients.TryRemove(connection.Id, out _);
            connection.Dispose();
            if (wasSubscriber)
            {
                await _logger.LogAsync($"Pipe subscriber disconnected: {connection.Id}", CancellationToken.None);
            }
        }
    }

    private async Task<PipeResponse> HandleRequestAsync(PipeRequest request, NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            PipeCommand.Ping => new PipeResponse
            {
                Success = true,
                Message = "Backend is reachable."
            },
            PipeCommand.EnsureTrayHost => HandleEnsureTrayHostRequest(),
            PipeCommand.SubscribeTrayHost => new PipeResponse
            {
                Success = true,
                Message = "Tray host subscription established."
            },
            PipeCommand.SubscribeNotifications => new PipeResponse
            {
                Success = true,
                Message = "Notification subscription established."
            },
            PipeCommand.RequestShutdown => HandleShutdownRequest(),
            PipeCommand.SetLogLevel when request.LogLevel is not null
                => HandleSetLogLevelRequest(request.LogLevel.Value),
            PipeCommand.GetSnapshot => new PipeResponse
            {
                Success = true,
                Message = "Snapshot loaded.",
                Items = await _catalog.GetSnapshotAsync(cancellationToken)
            },
            PipeCommand.GetSceneSnapshot when request.SceneKind is not null
                => new PipeResponse
                {
                    Success = true,
                    Message = "Scene snapshot loaded.",
                    Items = await _catalog.GetSceneSnapshotAsync(request.SceneKind.Value, request.ScopeValue, cancellationToken)
                },
            PipeCommand.SetEnhanceMenuItemEnabled when request.ScopeValue is not null && request.DefinitionXml is not null && request.Enable is not null
                => await _catalog.SetEnhanceMenuItemEnabledAsync(
                    request.ScopeValue,
                    request.DefinitionXml,
                    request.Enable.Value,
                    request.CultureName,
                    cancellationToken),
            PipeCommand.SetDetailedEditRuleValue
                => await _catalog.SetDetailedEditRuleValueAsync(
                    request.RuleStorageKind,
                    request.RulePath,
                    request.RuleSection,
                    request.RuleKeyName,
                    request.RuleValueKind,
                    request.RuleValue,
                    request.RuleUserSid,
                    cancellationToken),
            PipeCommand.AcknowledgeItemState when request.ItemId is not null
                => await _catalog.AcknowledgeItemStateAsync(request.ItemId, cancellationToken),
            PipeCommand.SetEnabled when request.ItemId is not null && request.Enable is not null
                => await _catalog.ApplyDesiredStateAsync(request.ItemId, request.Enable.Value, cancellationToken),
            PipeCommand.SetShellAttribute when request.ItemId is not null && request.Enable is not null && request.ShellAttribute is not null
                => await _catalog.ApplyShellAttributeAsync(request.ItemId, request.ShellAttribute.Value, request.Enable.Value, cancellationToken),
            PipeCommand.SetDisplayText when request.ItemId is not null && request.TextValue is not null
                => await _catalog.ApplyDisplayTextAsync(request.ItemId, request.TextValue, cancellationToken),
            PipeCommand.GetRegistryProtectionSetting
                => await _catalog.GetRegistryProtectionSettingAsync(cancellationToken),
            PipeCommand.SetRegistryProtectionSetting when request.Enable is not null
                => await _catalog.SetRegistryProtectionSettingAsync(request.Enable.Value, cancellationToken),
            PipeCommand.ApplyDecision when request.ItemId is not null && request.Decision is not null
                => await _catalog.ApplyDecisionAsync(
                    request.ItemId,
                    request.Decision.Value,
                    cancellationToken),
            PipeCommand.DeleteItem when request.ItemId is not null
                => await _catalog.DeleteItemAsync(request.ItemId, cancellationToken),
            PipeCommand.UndoDelete when request.ItemId is not null
                => await _catalog.UndoDeleteAsync(request.ItemId, cancellationToken),
            PipeCommand.PurgeDeletedItem when request.ItemId is not null
                => await _catalog.PurgeDeletedItemAsync(request.ItemId, cancellationToken),
            PipeCommand.GetSpecialMenuSnapshot when request.SpecialKind is not null
                => new PipeResponse
                {
                    Success = true,
                    Message = "Special menu snapshot loaded.",
                    SpecialItems = await _specialMenuService.GetSnapshotAsync(
                        request.SpecialKind.Value,
                        request.SpecialKind.Value == SpecialMenuKind.ShellNew
                            ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                            : null,
                        cancellationToken)
                },
            PipeCommand.SetSpecialMenuItemEnabled when request.SpecialItem is not null && request.Enable is not null
                => await _specialMenuService.SetEnabledAsync(
                    request.SpecialItem,
                    request.Enable.Value,
                    request.ClientOperationId,
                    request.SpecialItem.Kind == SpecialMenuKind.ShellNew
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.CreateSpecialMenuItem
                => await _specialMenuService.CreateAsync(
                    request,
                    request.SpecialKind == SpecialMenuKind.ShellNew
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.UpdateSpecialMenuItem
                => await _specialMenuService.UpdateAsync(
                    request,
                    request.SpecialKind == SpecialMenuKind.ShellNew
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.DeleteSpecialMenuItem when request.SpecialItem is not null
                => await _specialMenuService.DeleteAsync(
                    request.SpecialItem,
                    request.ClientOperationId,
                    request.SpecialItem.Kind == SpecialMenuKind.ShellNew
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.UndoSpecialMenuItem when request.SpecialItem is not null
                => await _specialMenuService.UndoDeleteAsync(
                    request.SpecialItem,
                    request.ClientOperationId,
                    request.SpecialItem.Kind == SpecialMenuKind.ShellNew
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.PurgeSpecialMenuItem when request.SpecialItem is not null
                => await _specialMenuService.PurgeDeletedAsync(
                    request.SpecialItem,
                    request.ClientOperationId,
                    request.SpecialItem.Kind == SpecialMenuKind.ShellNew
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.MoveSpecialMenuItem
                => await _specialMenuService.MoveAsync(
                    request,
                    request.ShellNewSort is not null
                        ? await ResolveShellNewUserContextAsync(stream, cancellationToken)
                        : null,
                    cancellationToken),
            PipeCommand.RestoreSpecialMenuDefaults when request.SpecialKind is not null
                => await _specialMenuService.RestoreDefaultsAsync(
                    request.SpecialKind.Value,
                    request.ScopeValue,
                    request.ClientOperationId,
                    null,
                    cancellationToken),
            PipeCommand.SetShellNewOrderLock when request.ShellNewLock is not null
                => await _specialMenuService.SetShellNewOrderLockAsync(
                    request.ShellNewLock.Lock,
                    request.ClientOperationId,
                    await ResolveShellNewUserContextAsync(stream, cancellationToken),
                    cancellationToken),
            PipeCommand.RepairShellNewOrderAcl
                => await _specialMenuService.RepairShellNewOrderAclAsync(
                    request.ClientOperationId,
                    await ResolveShellNewUserContextAsync(stream, cancellationToken),
                    cancellationToken),
            PipeCommand.AnalyzeFileTypeContext when request.FileTypeAnalysis is not null
                => new PipeResponse
                {
                    Success = true,
                    Message = "File type context analyzed.",
                    FileTypeAnalysisResults = await _fileTypeSceneMenuService.AnalyzeAsync(request.FileTypeAnalysis, cancellationToken)
                },
            PipeCommand.CreateSceneMenuItem when request.CreateSceneMenuItem is not null
                => await _fileTypeSceneMenuService.CreateSceneMenuItemAsync(
                    request.CreateSceneMenuItem,
                    request.ClientOperationId,
                    cancellationToken),
            PipeCommand.RestartExplorer
                => HandleRestartExplorerRequest(),
            PipeCommand.SetWin11BlockedItem when request.ItemId is not null
                => await _windows11BlocksService.SetWin11BlockedItemAsync(
                    request.ItemId,
                    request.DisplayName ?? string.Empty,
                    request.BlockMachine ?? false,
                    request.ClientOperationId,
                    null,
                    cancellationToken),
            PipeCommand.RemoveWin11BlockedItem when request.ItemId is not null
                => await _windows11BlocksService.RemoveWin11BlockedItemAsync(
                    request.ItemId,
                    request.UnblockMachine ?? false,
                    request.ClientOperationId,
                    null,
                    cancellationToken),
            PipeCommand.GetWin11BlockedItems
                => await _windows11BlocksService.GetWin11BlockedItemsAsync(
                    request.ClientOperationId,
                    null,
                    cancellationToken),
            PipeCommand.GetWin11ContextMenuSnapshot
                => new PipeResponse
                {
                    Success = true,
                    Message = "Win11 context menu snapshot loaded.",
                    Items = await _catalog.GetWindows11SnapshotAsync(cancellationToken),
                    ClientOperationId = request.ClientOperationId
                },
            PipeCommand.SetAutoStartEnabled when request.AutoStartEnabled is not null
                => await _autoStartService.SetAutoStartEnabledAsync(
                    request.AutoStartEnabled.Value,
                    request.ClientOperationId,
                    null,
                    cancellationToken),
            PipeCommand.GetAutoStartEnabled
                => await _autoStartService.GetAutoStartEnabledAsync(
                    request.ClientOperationId,
                    null,
                    cancellationToken),
            _ => new PipeResponse
            {
                Success = false,
                Message = "The request was missing required data."
            }
        };
    }

    private async Task<BackendUserContext?> ResolveShellNewUserContextAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        var resolver = new BackendUserContextResolver(_logger);
        var userContext = resolver.TryResolveFromPipeClient(stream)
            ?? resolver.TryResolveInteractiveUserFallback();
        if (userContext is null)
        {
            await _logger.LogAsync(RuntimeLogLevel.Warning, "ShellNew request could not resolve frontend user context.", cancellationToken);
        }

        return userContext;
    }

    private async Task BroadcastNotificationAsync(BackendNotification notification, CancellationToken cancellationToken)
    {
        var envelope = new PipeEnvelope
        {
            MessageType = PipeMessageType.Notification,
            Notification = notification
        };

        foreach (var connection in _clients.Values.Where(static connection => connection.IsNotificationSubscriber).ToList())
        {
            try
            {
                await connection.SendAsync(envelope, cancellationToken);
            }
            catch
            {
                _clients.TryRemove(connection.Id, out _);
                connection.Dispose();
            }
        }
    }

    /// <summary>
    /// Executes broadcast Service Stopping Async.
    /// </summary>
    public async Task BroadcastServiceStoppingAsync(CancellationToken cancellationToken)
    {
        await BroadcastNotificationAsync(
            new BackendNotification
            {
                Kind = PipeNotificationKind.ServiceStopping,
                Message = "Backend service is stopping.",
                Timestamp = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    private PipeResponse HandleShutdownRequest()
    {
        BackendShutdownRequested?.Invoke(this, EventArgs.Empty);
        return new PipeResponse
        {
            Success = true,
            Message = "Backend shutdown requested."
        };
    }

    private PipeResponse HandleEnsureTrayHostRequest()
    {
        EnsureTrayHostRequested?.Invoke(this, EventArgs.Empty);
        return new PipeResponse
        {
            Success = true,
            Message = "Tray host startup requested."
        };
    }

    private PipeResponse HandleSetLogLevelRequest(RuntimeLogLevel logLevel)
    {
        _logger.Configure(logLevel);
        _logger.LogFireAndForget($"Backend log level set to {logLevel}.");
        return new PipeResponse
        {
            Success = true,
            Message = $"Backend log level set to {logLevel}."
        };
    }

    private PipeResponse HandleRestartExplorerRequest()
    {
        return new PipeResponse
        {
            Success = false,
            Message = "Cannot restart explorer: user context is not available in the isolated ShellNew pipeline."
        };
    }

    private sealed class PipeClientConnection : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly NamedPipeServerStream _stream;

        /// <summary>
        /// Executes pipe Client Connection.
        /// </summary>
        public PipeClientConnection(NamedPipeServerStream stream)
        {
            Id = Guid.NewGuid();
            _stream = stream;
            Reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            Writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true
            };
        }

        /// <summary>
        /// Gets the id.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the reader.
        /// </summary>
        public StreamReader Reader { get; }

        /// <summary>
        /// Gets the writer.
        /// </summary>
        public StreamWriter Writer { get; }

        /// <summary>
        /// Gets or sets a value indicating whether notification Subscriber.
        /// </summary>
        public bool IsNotificationSubscriber { get; set; }

        /// <summary>
        /// Executes send Async.
        /// </summary>
        public async Task SendAsync(PipeEnvelope envelope, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(envelope, JsonOptions);

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await Writer.WriteLineAsync(payload).WaitAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Executes dispose.
        /// </summary>
        public void Dispose()
        {
            Reader.Dispose();
            Writer.Dispose();
            _stream.Dispose();
            _writeLock.Dispose();
        }
    }
}
