using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Handles file type scene analysis and scene-scoped menu creation.
/// </summary>
public sealed class FileTypeSceneMenuService
{
    private readonly ContextMenuRegistryCatalog _catalog;
    private readonly ContextMenuStateStore _stateStore;
    private readonly FileLogger _logger;

    public FileTypeSceneMenuService(
        ContextMenuRegistryCatalog catalog,
        ContextMenuStateStore stateStore,
        FileLogger logger)
    {
        _catalog = catalog;
        _stateStore = stateStore;
        _logger = logger;
    }

    public Task<IReadOnlyList<FileTypeAnalysisResult>> AnalyzeAsync(FileTypeAnalysisRequest request, CancellationToken cancellationToken)
    {
        var path = Environment.ExpandEnvironmentVariables(request.Path.Trim());
        var results = new List<FileTypeAnalysisResult>();

        if (File.Exists(path))
        {
            AnalyzeFile(path, results, includeLnkScene: true);
        }
        else if (Directory.Exists(path))
        {
            AnalyzeDirectory(path, results);
        }
        else
        {
            throw new FileNotFoundException("The selected file or folder does not exist.", path);
        }

        return Task.FromResult<IReadOnlyList<FileTypeAnalysisResult>>(results);
    }

    public async Task<PipeResponse> CreateSceneMenuItemAsync(
        CreateSceneMenuItemRequest request,
        Guid? operationId,
        BackendUserContext? userContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var relativeRoot = ResolveSceneRoot(request.SceneKind, request.ScopeValue);
            if (string.IsNullOrWhiteSpace(relativeRoot))
            {
                return new PipeResponse
                {
                    Success = false,
                    Message = "The target scene is missing a required scope value.",
                    ClientOperationId = operationId
                };
            }

            var keyName = SanitizeKeyName(request.KeyName);
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return Failure("The key name is required.", operationId);
            }

            if (request.ItemKind == SceneMenuItemKind.ShellVerb)
            {
                if (string.IsNullOrWhiteSpace(request.Command))
                {
                    return Failure("Shell verb items require a command.", operationId);
                }

                WriteShellVerb(relativeRoot, keyName, request);
            }
            else
            {
                if (!Guid.TryParse(request.GuidText, out var guid))
                {
                    return Failure("The GUID format is invalid.", operationId);
                }

                Registry.SetValue(
                    $@"HKEY_CLASSES_ROOT\{relativeRoot}\shellex\ContextMenuHandlers\{keyName}",
                    string.Empty,
                    guid.ToString("B"),
                    RegistryValueKind.String);
            }

            ShellChangeNotifier.NotifyAssociationsChanged();
            var snapshot = await _catalog.GetSceneSnapshotAsync(request.SceneKind, request.ScopeValue, cancellationToken);
            var created = snapshot.FirstOrDefault(item => string.Equals(item.KeyName, keyName, StringComparison.OrdinalIgnoreCase))
                ?? snapshot.FirstOrDefault(item => string.Equals(item.HandlerClsid, request.GuidText, StringComparison.OrdinalIgnoreCase));

            if (created is not null)
            {
                await SuppressDetectionAsync(created, cancellationToken);
            }

            await _logger.LogAsync($"Created scene menu item. Scene={request.SceneKind}, Scope={request.ScopeValue}, KeyName={keyName}.", cancellationToken);
            return new PipeResponse
            {
                Success = true,
                Message = "Scene menu item created.",
                Item = created,
                ClientOperationId = operationId
            };
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(RuntimeLogLevel.Error, $"Failed to create scene menu item: {ex.Message}", cancellationToken);
            return Failure(ex.Message, operationId);
        }
    }

    private static void AnalyzeFile(string filePath, List<FileTypeAnalysisResult> results, bool includeLnkScene)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".";
        }

        if (includeLnkScene && extension == ".lnk")
        {
            try
            {
                var shortcut = ShortcutFile.Read(filePath);
                if (File.Exists(shortcut.TargetPath))
                {
                    AnalyzeFile(shortcut.TargetPath, results, includeLnkScene: false);
                }
                else if (Directory.Exists(shortcut.TargetPath))
                {
                    AnalyzeDirectory(shortcut.TargetPath, results);
                }
            }
            catch
            {
            }

            results.Add(new FileTypeAnalysisResult
            {
                SceneKind = ContextMenuSceneKind.LnkFile,
                ScopeValue = "lnkfile",
                DisplayName = "Shortcut (.lnk)",
                Reason = "The selected item is a shortcut."
            });
            return;
        }

        results.Add(new FileTypeAnalysisResult
        {
            SceneKind = ContextMenuSceneKind.CustomRegistryPath,
            ScopeValue = @"HKCR\*\shell",
            DisplayName = "All files (*)",
            Reason = "Applies to every file."
        });
        results.Add(new FileTypeAnalysisResult
        {
            SceneKind = ContextMenuSceneKind.CustomRegistryPath,
            ScopeValue = @"HKCR\AllFilesystemObjects\shell",
            DisplayName = "All filesystem objects",
            Reason = "Applies to files and folders."
        });

        if (extension == ".exe")
        {
            results.Add(new FileTypeAnalysisResult
            {
                SceneKind = ContextMenuSceneKind.ExeFile,
                ScopeValue = "exefile",
                DisplayName = "Executable files",
                Reason = "The target file is an executable."
            });
        }
        else
        {
            results.Add(new FileTypeAnalysisResult
            {
                SceneKind = ContextMenuSceneKind.CustomExtension,
                ScopeValue = extension,
                DisplayName = $"Extension {extension}",
                Reason = "Applies to this extension."
            });
        }

        using var extensionKey = Registry.ClassesRoot.OpenSubKey(extension, writable: false);
        var progId = extensionKey?.GetValue(null)?.ToString();
        if (string.IsNullOrWhiteSpace(progId))
        {
            results.Add(new FileTypeAnalysisResult
            {
                SceneKind = ContextMenuSceneKind.UnknownType,
                ScopeValue = "Unknown",
                DisplayName = "Unknown file type",
                Reason = "The extension has no ProgId/open mode."
            });
        }

        var perceivedType = extensionKey?.GetValue("PerceivedType")?.ToString();
        if (!string.IsNullOrWhiteSpace(perceivedType))
        {
            results.Add(new FileTypeAnalysisResult
            {
                SceneKind = ContextMenuSceneKind.PerceivedType,
                ScopeValue = perceivedType,
                DisplayName = $"Perceived type {perceivedType}",
                Reason = "Applies to files with this perceived type."
            });
        }
    }

    private static void AnalyzeDirectory(string directoryPath, List<FileTypeAnalysisResult> results)
    {
        results.Add(new FileTypeAnalysisResult
        {
            SceneKind = ContextMenuSceneKind.CustomRegistryPath,
            ScopeValue = @"HKCR\Folder\shell",
            DisplayName = "Folders",
            Reason = "Applies to shell folders."
        });

        if (Path.GetPathRoot(directoryPath)?.TrimEnd('\\').Equals(directoryPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
        {
            results.Add(new FileTypeAnalysisResult
            {
                SceneKind = ContextMenuSceneKind.CustomRegistryPath,
                ScopeValue = @"HKCR\Drive\shell",
                DisplayName = "Drives",
                Reason = "The selected folder is a drive root."
            });
            return;
        }

        results.Add(new FileTypeAnalysisResult
        {
            SceneKind = ContextMenuSceneKind.CustomRegistryPath,
            ScopeValue = @"HKCR\Directory\shell",
            DisplayName = "Directories",
            Reason = "Applies to file-system directories."
        });
        results.Add(new FileTypeAnalysisResult
        {
            SceneKind = ContextMenuSceneKind.CustomRegistryPath,
            ScopeValue = @"HKCR\AllFilesystemObjects\shell",
            DisplayName = "All filesystem objects",
            Reason = "Applies to files and folders."
        });
        results.Add(new FileTypeAnalysisResult
        {
            SceneKind = ContextMenuSceneKind.DirectoryType,
            ScopeValue = "Document",
            DisplayName = "Directory type",
            Reason = "Use a specific directory type if one matches this folder."
        });
    }

    private async Task SuppressDetectionAsync(ContextMenuEntry created, CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken);
        var state = PersistedContextMenuState.FromEntry(created);
        state.ObservedEnabled = created.IsEnabled;
        state.DesiredEnabled = created.IsEnabled;
        state.SuppressNextDetection = true;
        state.IsPendingApproval = false;
        states[created.Id] = state;
        await _stateStore.SaveAsync(states, cancellationToken);
    }

    private static void WriteShellVerb(string relativeRoot, string keyName, CreateSceneMenuItemRequest request)
    {
        var path = $@"HKEY_CLASSES_ROOT\{relativeRoot}\shell\{keyName}";
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            Registry.SetValue(path, "MUIVerb", request.DisplayName, RegistryValueKind.String);
        }

        if (!string.IsNullOrWhiteSpace(request.Icon))
        {
            Registry.SetValue(path, "Icon", request.Icon, RegistryValueKind.String);
        }

        SetFlag(path, "Extended", request.Extended);
        SetFlag(path, "OnlyInBrowserWindow", request.OnlyInBrowserWindow);
        SetFlag(path, "NoWorkingDirectory", request.NoWorkingDirectory);
        SetFlag(path, "NeverDefault", request.NeverDefault);
        SetFlag(path, "ShowAsDisabledIfHidden", request.ShowAsDisabledIfHidden);
        Registry.SetValue($@"{path}\command", string.Empty, request.Command ?? string.Empty, RegistryValueKind.String);
    }

    private static void SetFlag(string path, string valueName, bool enabled)
    {
        if (enabled)
        {
            Registry.SetValue(path, valueName, string.Empty, RegistryValueKind.String);
        }
    }

    private static string? ResolveSceneRoot(ContextMenuSceneKind sceneKind, string? scopeValue)
    {
        return sceneKind switch
        {
            ContextMenuSceneKind.LnkFile => "lnkfile",
            ContextMenuSceneKind.UwpShortcut => "Launcher.ImmersiveApplication",
            ContextMenuSceneKind.ExeFile => @"SystemFileAssociations\.exe",
            ContextMenuSceneKind.UnknownType => "Unknown",
            ContextMenuSceneKind.CustomExtension => string.IsNullOrWhiteSpace(scopeValue) ? null : $@"SystemFileAssociations\{NormalizeExtension(scopeValue)}",
            ContextMenuSceneKind.PerceivedType => string.IsNullOrWhiteSpace(scopeValue) ? null : $@"SystemFileAssociations\{scopeValue.Trim()}",
            ContextMenuSceneKind.DirectoryType => string.IsNullOrWhiteSpace(scopeValue) ? null : $@"SystemFileAssociations\Directory.{scopeValue.Trim()}",
            ContextMenuSceneKind.CustomRegistryPath => NormalizeClassesRootRelativePath(scopeValue)?.Replace(@"\shell", string.Empty, StringComparison.OrdinalIgnoreCase),
            _ => null
        };
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

    private static string SanitizeKeyName(string value)
    {
        foreach (var invalid in new[] { '\\', '/', '*', '?', '"', '<', '>', '|' })
        {
            value = value.Replace(invalid.ToString(), string.Empty, StringComparison.Ordinal);
        }

        return value.Trim();
    }

    private static string? NormalizeClassesRootRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace('/', '\\').Trim('\\');
        foreach (var prefix in new[] { @"HKEY_CLASSES_ROOT\", @"HKCR\" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized;
    }

    private static PipeResponse Failure(string message, Guid? operationId) => new()
    {
        Success = false,
        Message = message,
        ClientOperationId = operationId
    };
}
