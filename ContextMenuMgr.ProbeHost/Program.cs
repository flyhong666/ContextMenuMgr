using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.ProbeHost;

internal static class Program
{
    private const uint CmfNormal = 0;
    private const uint CmfExtendedVerbs = 0x00000100;
    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;
    private const uint GcsVerbW = 4;
    private const uint GcsHelpTextW = 5;
    private const uint ShgdnForParsing = 0x8000;
    private const uint MiimId = 0x00000002;
    private const uint MiimSubmenu = 0x00000004;
    private const uint MiimFtype = 0x00000100;
    private const uint MftSeparator = 0x00000800;
    private const int HResultNoInterface = unchecked((int)0x80004002);
    private const uint ClsctxInprocServer = 0x1;
    private const uint ClsctxLocalServer = 0x4;
    private const uint ClsctxAll = ClsctxInprocServer | ClsctxLocalServer;
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidDataObject = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid IidShellExtInit = new("000214E8-0000-0000-C000-000000000046");

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = utf8NoBom;
        Console.OutputEncoding = utf8NoBom;
        Console.Error.WriteLine($"ProbeHostStart: ProcessArchitecture={RuntimeInformation.ProcessArchitecture}, OSArchitecture={RuntimeInformation.OSArchitecture}, Is64BitProcess={Environment.Is64BitProcess}.");

        ContextMenuDeepAnalysisRequest? request = null;
        var resultPath = GetArgumentValue(args, "--result");

        try
        {
            var requestPath = GetArgumentValue(args, "--request");
            var json = !string.IsNullOrWhiteSpace(requestPath)
                ? await File.ReadAllTextAsync(requestPath, utf8NoBom)
                : await Console.In.ReadToEndAsync();

            request = JsonSerializer.Deserialize<ContextMenuDeepAnalysisRequest>(json, JsonOptions);
            if (request is null)
            {
                var invalidRequest = WithDiagnostics(Fail("InvalidRequest", "Request JSON was empty or invalid."));
                await WriteResultAsync(invalidRequest, resultPath, utf8NoBom);
                return 2;
            }

            Console.Error.WriteLine($"ProbeHostRequestParsed: OperationId={request.OperationId}, ItemId={request.ItemId}, Category={request.Category}, EntryKind={request.EntryKind}, HandlerClsid={request.HandlerClsid}.");
            var result = WithDiagnostics(Probe(request));
            await WriteResultAsync(result, resultPath, utf8NoBom);
            return result.Success ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ProbeHostUnhandledException: {ex}");
            var result = WithDiagnostics(request is null
                ? Fail(
                    "UnhandledException",
                    ex.ToString(),
                    request?.DisplayName,
                    request?.HandlerClsid,
                    request?.HandlerFilePath,
                    request?.SamplePath)
                : Fail("UnhandledException", ex.Message, request, diagnosticDetails: ex.ToString()));
            await WriteResultAsync(result, resultPath, utf8NoBom);
            return 1;
        }
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static ContextMenuDeepAnalysisResult Probe(ContextMenuDeepAnalysisRequest request)
    {
        Console.Error.WriteLine($"OperationId={request.OperationId}, ProbeMode={request.ProbeMode}.");
        Console.Error.WriteLine("ProbeStage=ValidateRequest.");
        if (request.EntryKind != ContextMenuEntryKind.ShellExtension)
        {
            return Fail("UnsupportedEntryKind", "Only Shell Extension entries can be probed.", request);
        }

        if (string.IsNullOrWhiteSpace(request.HandlerClsid) || !Guid.TryParse(request.HandlerClsid, out _))
        {
            return Fail("InvalidHandlerClsid", "Handler CLSID is missing or invalid.", request);
        }

        var handlerArchitecture = ResolveHandlerArchitecture(request);
        Console.Error.WriteLine(
            $"ProbeArchitecture: ProcessArchitecture={RuntimeInformation.ProcessArchitecture}, OSArchitecture={RuntimeInformation.OSArchitecture}, HandlerFilePath={handlerArchitecture.FilePath ?? "<null>"}, HandlerFileExists={handlerArchitecture.Exists}, HandlerFileMachineType={handlerArchitecture.MachineType}, Compatibility={handlerArchitecture.Compatibility}.");
        if (handlerArchitecture.Compatibility == "Mismatch")
        {
            return Fail(
                "ArchitectureMismatch",
                "The shell extension appears to use a different process architecture from the analysis helper. Runtime probing is skipped to avoid crashing.",
                request) with
            {
                HandlerFilePath = handlerArchitecture.FilePath ?? request.HandlerFilePath,
                HandlerFileExists = handlerArchitecture.Exists,
                HandlerFileMachineType = handlerArchitecture.MachineType,
                ArchitectureCompatibility = handlerArchitecture.Compatibility
            };
        }

        if (request.ProbeMode == ContextMenuDeepAnalysisProbeMode.WholeContextMenu)
        {
            return ProbeWholeContextMenu(request, handlerArchitecture);
        }

        return ProbeSpecificHandler(request, handlerArchitecture);
    }

    private static ContextMenuDeepAnalysisResult ProbeSpecificHandler(
        ContextMenuDeepAnalysisRequest request,
        HandlerArchitectureInfo handlerArchitecture)
    {
        Console.Error.WriteLine("ProbeStage=CreateSample.");
        var sample = CreateSample(request);
        if (sample is null)
        {
            return Fail("UnsupportedCategory", "Probing is not supported for this category.", request);
        }

        nint handlerUnknownPtr = nint.Zero;
        nint shellExtInitPtr = nint.Zero;
        nint contextMenuPtr = nint.Zero;
        object? shellExtInitObject = null;
        object? contextMenuObject = null;
        DataObjectLease? dataObjectLease = null;
        nint hmenu = nint.Zero;

        try
        {
            Console.Error.WriteLine($"ProbeSampleReady: Path={sample.Path}, Background={sample.Background}.");
            var handlerClsid = Guid.Parse(request.HandlerClsid!);
            Console.Error.WriteLine($"ProbeStage=CoCreateHandlerStart HandlerClsid={request.HandlerClsid}.");
            var iidUnknown = IidUnknown;
            var hr = NativeMethods.CoCreateInstance(ref handlerClsid, nint.Zero, ClsctxInprocServer, ref iidUnknown, out handlerUnknownPtr);
            Console.Error.WriteLine($"ProbeStage=CoCreateHandlerEnd Hr=0x{hr:X8}, HandlerPtr=0x{handlerUnknownPtr:X}.");
            if (hr < 0 || handlerUnknownPtr == nint.Zero)
            {
                var code = hr == HResultNoInterface ? "CoCreateHandlerNoIUnknown" : "CoCreateHandlerFailed";
                return Fail(
                    code,
                    FriendlySpecificHandlerMessage(code),
                    request,
                    sample.Path,
                    BuildStageDiagnostic("CoCreateInstance(IUnknown)", hr));
            }

            Console.Error.WriteLine("ProbeStage=QueryIShellExtInitStart.");
            var iidShellExtInit = IidShellExtInit;
            hr = Marshal.QueryInterface(handlerUnknownPtr, in iidShellExtInit, out shellExtInitPtr);
            Console.Error.WriteLine($"ProbeStage=QueryIShellExtInitEnd Hr=0x{hr:X8}, ShellExtInitPtr=0x{shellExtInitPtr:X}.");
            if (hr < 0 || shellExtInitPtr == nint.Zero)
            {
                return Fail(
                    "ShellExtInitNotSupported",
                    FriendlySpecificHandlerMessage("ShellExtInitNotSupported"),
                    request,
                    sample.Path,
                    BuildStageDiagnostic("QueryInterface(IShellExtInit)", hr));
            }

            Console.Error.WriteLine("ProbeStage=CreateDataObjectStart.");
            dataObjectLease = CreateInitializationContext(sample.Path, sample.Background);
            Console.Error.WriteLine($"ProbeStage=CreateDataObjectEnd Hr=0x00000000, PidlFolder=0x{dataObjectLease.PidlFolder:X}, DataObjectPtr=0x{dataObjectLease.DataObjectPtr:X}.");

            shellExtInitObject = Marshal.GetObjectForIUnknown(shellExtInitPtr);
            var shellExtInit = (IShellExtInit)shellExtInitObject;
            Console.Error.WriteLine("ProbeStage=ShellExtInitInitializeStart.");
            hr = shellExtInit.Initialize(dataObjectLease.PidlFolder, dataObjectLease.DataObjectPtr, nint.Zero);
            Console.Error.WriteLine($"ProbeStage=ShellExtInitInitializeEnd Hr=0x{hr:X8}.");
            if (hr < 0)
            {
                var code = sample.Background ? "SpecificHandlerBackgroundInitializationFailed" : "ShellExtInitInitializeFailed";
                return Fail(
                    code,
                    FriendlySpecificHandlerMessage(code),
                    request,
                    sample.Path,
                    BuildStageDiagnostic("IShellExtInit.Initialize", hr));
            }

            Console.Error.WriteLine("ProbeStage=QueryIContextMenuStart.");
            var iidContextMenu = IidContextMenu;
            hr = Marshal.QueryInterface(handlerUnknownPtr, in iidContextMenu, out contextMenuPtr);
            Console.Error.WriteLine($"ProbeStage=QueryIContextMenuEnd Hr=0x{hr:X8}, ContextMenuPtr=0x{contextMenuPtr:X}.");
            if (hr < 0 || contextMenuPtr == nint.Zero)
            {
                return Fail(
                    "IContextMenuNotSupported",
                    FriendlySpecificHandlerMessage("IContextMenuNotSupported"),
                    request,
                    sample.Path,
                    BuildStageDiagnostic("QueryInterface(IContextMenu)", hr));
            }

            contextMenuObject = Marshal.GetObjectForIUnknown(contextMenuPtr);
            var contextMenu = (IContextMenu)contextMenuObject;
            Console.Error.WriteLine("ProbeStage=CreatePopupMenuStart.");
            hmenu = NativeMethods.CreatePopupMenu();
            Console.Error.WriteLine($"ProbeStage=CreatePopupMenuEnd Hmenu=0x{hmenu:X}.");
            if (hmenu == nint.Zero)
            {
                return Fail("CreatePopupMenuFailed", "CreatePopupMenu failed.", request, sample.Path);
            }

            var flags = request.IncludeExtendedVerbs ? CmfNormal | CmfExtendedVerbs : CmfNormal;
            Console.Error.WriteLine($"ProbeStage=SpecificQueryContextMenuStart Flags=0x{flags:X}.");
            hr = contextMenu.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, flags);
            Console.Error.WriteLine($"ProbeStage=SpecificQueryContextMenuEnd Hr=0x{hr:X8}.");
            if (hr < 0)
            {
                return Fail(
                    "QueryContextMenuFailed",
                    FriendlySpecificHandlerMessage("QueryContextMenuFailed"),
                    request,
                    sample.Path,
                    BuildStageDiagnostic("IContextMenu.QueryContextMenu", hr));
            }

            Console.Error.WriteLine("ProbeStage=EnumerateMenuStart.");
            var items = EnumerateMenu(hmenu, contextMenu);
            Console.Error.WriteLine($"ProbeStage=EnumerateMenuEnd Count={items.Count}.");
            if (!items.Any(static item => !item.IsSeparator))
            {
                return Fail(
                    "SpecificHandlerReturnedNoItems",
                    FriendlySpecificHandlerMessage("SpecificHandlerReturnedNoItems"),
                    request,
                    sample.Path,
                    "Stage=EnumerateMenu; ItemCount=0");
            }

            return SuccessResult(request, sample.Path, handlerArchitecture, items) with
            {
                ProbeMode = ContextMenuDeepAnalysisProbeMode.SpecificHandler,
                IsSpecificHandlerResult = true,
                IsWholeContextMenuResult = false
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ProbeStage=Exception, Exception={ex}");
            if (ex is ShellProbeException shellEx)
            {
                return Fail($"{shellEx.Operation}Failed", shellEx.Message, request, sample.Path);
            }

            return Fail("ProbeFailed", ex.Message, request, sample.Path);
        }
        finally
        {
            if (hmenu != nint.Zero)
            {
                NativeMethods.DestroyMenu(hmenu);
            }

            if (contextMenuObject is not null)
            {
                Marshal.FinalReleaseComObject(contextMenuObject);
            }

            if (shellExtInitObject is not null)
            {
                Marshal.FinalReleaseComObject(shellExtInitObject);
            }

            if (contextMenuPtr != nint.Zero)
            {
                Marshal.Release(contextMenuPtr);
            }

            if (shellExtInitPtr != nint.Zero)
            {
                Marshal.Release(shellExtInitPtr);
            }

            if (handlerUnknownPtr != nint.Zero)
            {
                Marshal.Release(handlerUnknownPtr);
            }

            dataObjectLease?.Dispose();
            sample.Dispose();
        }
    }

    private static ContextMenuDeepAnalysisResult ProbeWholeContextMenu(
        ContextMenuDeepAnalysisRequest request,
        HandlerArchitectureInfo handlerArchitecture)
    {
        Console.Error.WriteLine("ProbeStage=CreateSample.");
        var sample = CreateSample(request);
        if (sample is null)
        {
            return Fail("UnsupportedCategory", "Probing is not supported for this category.", request);
        }

        try
        {
            Console.Error.WriteLine($"ProbeSampleReady: Path={sample.Path}, Background={sample.Background}.");
            Console.Error.WriteLine("ProbeStage=CreateContextMenu.");
            using var contextMenuLease = CreateContextMenu(sample.Path, sample.Background);
            nint hmenu = nint.Zero;
            try
            {
                Console.Error.WriteLine("ProbeStage=CreatePopupMenuStart.");
                hmenu = NativeMethods.CreatePopupMenu();
                Console.Error.WriteLine($"ProbeStage=CreatePopupMenuEnd Hmenu=0x{hmenu:X}.");
                if (hmenu == nint.Zero)
                {
                    return Fail("CreatePopupMenuFailed", "CreatePopupMenu failed.", request, sample.Path);
                }

                var flags = request.IncludeExtendedVerbs ? CmfNormal | CmfExtendedVerbs : CmfNormal;
                Console.Error.WriteLine($"ProbeStage=QueryContextMenuStart Flags=0x{flags:X}.");
                var hr = contextMenuLease.ContextMenu.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, flags);
                Console.Error.WriteLine($"ProbeStage=QueryContextMenuEnd Hr=0x{hr:X8}.");
                if (hr < 0)
                {
                    return Fail("QueryContextMenuFailed", $"IContextMenu.QueryContextMenu failed: 0x{hr:X8}.", request, sample.Path);
                }

                Console.Error.WriteLine("ProbeStage=EnumerateMenuStart.");
                var items = EnumerateMenu(hmenu, contextMenuLease.ContextMenu);
                Console.Error.WriteLine($"ProbeStage=EnumerateMenuEnd Count={items.Count}.");
                return SuccessResult(request, sample.Path, handlerArchitecture, items) with
                {
                    ProbeMode = ContextMenuDeepAnalysisProbeMode.WholeContextMenu,
                    IsSpecificHandlerResult = false,
                    IsWholeContextMenuResult = true
                };
            }
            finally
            {
                if (hmenu != nint.Zero)
                {
                    NativeMethods.DestroyMenu(hmenu);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ProbeStage=Exception, Exception={ex}");
            if (ex is ShellProbeException shellEx)
            {
                return Fail($"{shellEx.Operation}Failed", shellEx.Message, request, sample.Path);
            }

            return Fail("ProbeFailed", ex.Message, request, sample.Path);
        }
        finally
        {
            sample.Dispose();
        }
    }

    private static ContextMenuDeepAnalysisResult SuccessResult(
        ContextMenuDeepAnalysisRequest request,
        string samplePath,
        HandlerArchitectureInfo handlerArchitecture,
        IReadOnlyList<ContextMenuDeepAnalysisMenuItem> items)
    {
        return new ContextMenuDeepAnalysisResult
        {
            OperationId = request.OperationId,
            Success = true,
            DisplayName = request.DisplayName,
            HandlerClsid = request.HandlerClsid,
            HandlerFilePath = handlerArchitecture.FilePath ?? request.HandlerFilePath,
            SamplePath = samplePath,
            HandlerFileExists = handlerArchitecture.Exists,
            HandlerFileMachineType = handlerArchitecture.MachineType,
            ArchitectureCompatibility = handlerArchitecture.Compatibility,
            ProbeMode = request.ProbeMode,
            IsSpecificHandlerResult = request.ProbeMode == ContextMenuDeepAnalysisProbeMode.SpecificHandler,
            IsWholeContextMenuResult = request.ProbeMode == ContextMenuDeepAnalysisProbeMode.WholeContextMenu,
            Items = items
        };
    }

    private static SampleContext? CreateSample(ContextMenuDeepAnalysisRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SamplePath) && (File.Exists(request.SamplePath) || Directory.Exists(request.SamplePath)))
        {
            return new SampleContext(request.SamplePath, IsBackgroundCategory(request.Category), null);
        }

        var root = Path.Combine(Path.GetTempPath(), "ContextMenuMgr.ProbeHost", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return request.Category switch
        {
            ContextMenuCategory.File or ContextMenuCategory.AllFileSystemObjects =>
                new SampleContext(Path.Combine(root, "sample.txt"), false, root, createFile: true),
            ContextMenuCategory.Folder or ContextMenuCategory.Directory =>
                new SampleContext(Path.Combine(root, "SampleFolder"), false, root, createDirectory: true),
            ContextMenuCategory.DirectoryBackground =>
                new SampleContext(root, true, root),
            ContextMenuCategory.DesktopBackground =>
                new SampleContext(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), true, root),
            _ => null
        };
    }

    private static bool IsBackgroundCategory(ContextMenuCategory category) =>
        category is ContextMenuCategory.DirectoryBackground or ContextMenuCategory.DesktopBackground;

    private static ContextMenuLease CreateContextMenu(string path, bool background)
    {
        nint absolutePidl = nint.Zero;
        nint folderPtr = nint.Zero;
        nint contextMenuPtr = nint.Zero;
        object? folderObject = null;
        object? contextMenuObject = null;

        Console.Error.WriteLine("ProbeStage=SHParseDisplayNameStart.");
        var hr = NativeMethods.SHParseDisplayName(path, nint.Zero, out absolutePidl, 0, out _);
        Console.Error.WriteLine($"ProbeStage=SHParseDisplayNameEnd Hr=0x{hr:X8}, Pidl=0x{absolutePidl:X}.");
        if (hr < 0 || absolutePidl == nint.Zero)
        {
            ThrowIfFailed(hr, "SHParseDisplayName");
        }

        try
        {
            if (background)
            {
                folderPtr = BindToFolder(absolutePidl);
                folderObject = Marshal.GetObjectForIUnknown(folderPtr);
                var folder = (IShellFolder)folderObject;
                var iidContextMenu = IidContextMenu;
                Console.Error.WriteLine("ProbeStage=CreateViewObjectStart.");
                hr = folder.CreateViewObject(nint.Zero, ref iidContextMenu, out contextMenuPtr);
                Console.Error.WriteLine($"ProbeStage=CreateViewObjectEnd Hr=0x{hr:X8}, ContextMenuPtr=0x{contextMenuPtr:X}.");
                if (hr < 0 || contextMenuPtr == nint.Zero)
                {
                    ThrowIfFailed(hr, "CreateViewObject");
                }

                contextMenuObject = Marshal.GetObjectForIUnknown(contextMenuPtr);
                return new ContextMenuLease(
                    (IContextMenu)contextMenuObject,
                    contextMenuObject,
                    folderObject,
                    contextMenuPtr,
                    folderPtr,
                    absolutePidl);
            }

            var iidShellFolder = IidShellFolder;
            Console.Error.WriteLine("ProbeStage=SHBindToParentStart.");
            hr = NativeMethods.SHBindToParent(absolutePidl, ref iidShellFolder, out folderPtr, out var childPidl);
            Console.Error.WriteLine($"ProbeStage=SHBindToParentEnd Hr=0x{hr:X8}, ParentPtr=0x{folderPtr:X}, ChildPidl=0x{childPidl:X}.");
            if (hr < 0 || folderPtr == nint.Zero || childPidl == nint.Zero)
            {
                ThrowIfFailed(hr, "SHBindToParent");
            }

            folderObject = Marshal.GetObjectForIUnknown(folderPtr);
            var parent = (IShellFolder)folderObject;
            var children = new[] { childPidl };
            var iid = IidContextMenu;
            Console.Error.WriteLine("ProbeStage=GetUIObjectOfStart.");
            hr = parent.GetUIObjectOf(nint.Zero, 1, children, ref iid, nint.Zero, out contextMenuPtr);
            Console.Error.WriteLine($"ProbeStage=GetUIObjectOfEnd Hr=0x{hr:X8}, ContextMenuPtr=0x{contextMenuPtr:X}.");
            if (hr < 0 || contextMenuPtr == nint.Zero)
            {
                ThrowIfFailed(hr, "GetUIObjectOf");
            }

            contextMenuObject = Marshal.GetObjectForIUnknown(contextMenuPtr);
            return new ContextMenuLease(
                (IContextMenu)contextMenuObject,
                contextMenuObject,
                folderObject,
                contextMenuPtr,
                folderPtr,
                absolutePidl);
        }
        catch
        {
            if (contextMenuObject is not null)
            {
                Marshal.FinalReleaseComObject(contextMenuObject);
            }

            if (folderObject is not null)
            {
                Marshal.FinalReleaseComObject(folderObject);
            }

            if (contextMenuPtr != nint.Zero)
            {
                Marshal.Release(contextMenuPtr);
            }

            if (folderPtr != nint.Zero)
            {
                Marshal.Release(folderPtr);
            }

            if (absolutePidl != nint.Zero)
            {
                NativeMethods.CoTaskMemFree(absolutePidl);
            }

            throw;
        }
    }

    private static DataObjectLease CreateInitializationContext(string path, bool background)
    {
        if (background)
        {
            nint folderPidl = nint.Zero;
            Console.Error.WriteLine("ProbeStage=SHParseDisplayNameStart.");
            var hr = NativeMethods.SHParseDisplayName(path, nint.Zero, out folderPidl, 0, out _);
            Console.Error.WriteLine($"ProbeStage=SHParseDisplayNameEnd Hr=0x{hr:X8}, FolderPidl=0x{folderPidl:X}.");
            if (hr < 0 || folderPidl == nint.Zero)
            {
                ThrowIfFailed(hr, "SHParseDisplayName");
            }

            return new DataObjectLease(folderPidl, folderPidl, nint.Zero, nint.Zero, null, nint.Zero);
        }

        nint absolutePidl = nint.Zero;
        nint folderPidlForInitialize = nint.Zero;
        nint parentFolderPtr = nint.Zero;
        nint dataObjectPtr = nint.Zero;
        object? parentFolderObject = null;
        try
        {
            Console.Error.WriteLine("ProbeStage=SHParseDisplayNameStart.");
            var hr = NativeMethods.SHParseDisplayName(path, nint.Zero, out absolutePidl, 0, out _);
            Console.Error.WriteLine($"ProbeStage=SHParseDisplayNameEnd Hr=0x{hr:X8}, Pidl=0x{absolutePidl:X}.");
            if (hr < 0 || absolutePidl == nint.Zero)
            {
                ThrowIfFailed(hr, "SHParseDisplayName");
            }

            var parentPath = Directory.Exists(path)
                ? Directory.GetParent(path)?.FullName
                : Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentPath))
            {
                Console.Error.WriteLine("ProbeStage=SHParseDisplayNameParentStart.");
                hr = NativeMethods.SHParseDisplayName(parentPath, nint.Zero, out folderPidlForInitialize, 0, out _);
                Console.Error.WriteLine($"ProbeStage=SHParseDisplayNameParentEnd Hr=0x{hr:X8}, FolderPidl=0x{folderPidlForInitialize:X}.");
                if (hr < 0)
                {
                    folderPidlForInitialize = nint.Zero;
                }
            }

            var iidShellFolder = IidShellFolder;
            Console.Error.WriteLine("ProbeStage=SHBindToParentStart.");
            hr = NativeMethods.SHBindToParent(absolutePidl, ref iidShellFolder, out parentFolderPtr, out var childPidl);
            Console.Error.WriteLine($"ProbeStage=SHBindToParentEnd Hr=0x{hr:X8}, ParentPtr=0x{parentFolderPtr:X}, ChildPidl=0x{childPidl:X}.");
            if (hr < 0 || parentFolderPtr == nint.Zero || childPidl == nint.Zero)
            {
                ThrowIfFailed(hr, "SHBindToParent");
            }

            parentFolderObject = Marshal.GetObjectForIUnknown(parentFolderPtr);
            var parentFolder = (IShellFolder)parentFolderObject;
            var children = new[] { childPidl };
            var iidDataObject = IidDataObject;
            hr = parentFolder.GetUIObjectOf(nint.Zero, 1, children, ref iidDataObject, nint.Zero, out dataObjectPtr);
            Console.Error.WriteLine($"ProbeStage=CreateDataObjectEnd Hr=0x{hr:X8}, DataObjectPtr=0x{dataObjectPtr:X}.");
            if (hr < 0 || dataObjectPtr == nint.Zero)
            {
                ThrowIfFailed(hr, "CreateDataObject");
            }

            return new DataObjectLease(folderPidlForInitialize, absolutePidl, parentFolderPtr, dataObjectPtr, parentFolderObject, childPidl);
        }
        catch
        {
            if (dataObjectPtr != nint.Zero)
            {
                Marshal.Release(dataObjectPtr);
            }

            if (parentFolderObject is not null)
            {
                Marshal.FinalReleaseComObject(parentFolderObject);
            }

            if (parentFolderPtr != nint.Zero)
            {
                Marshal.Release(parentFolderPtr);
            }

            if (absolutePidl != nint.Zero)
            {
                NativeMethods.CoTaskMemFree(absolutePidl);
            }

            if (folderPidlForInitialize != nint.Zero)
            {
                NativeMethods.CoTaskMemFree(folderPidlForInitialize);
            }

            throw;
        }
    }

    private static nint BindToFolder(nint pidl)
    {
        var iidShellFolder = IidShellFolder;
        var bindCtx = nint.Zero;
        Console.Error.WriteLine("ProbeStage=SHBindToObjectStart.");
        var hr = NativeMethods.SHBindToObject(nint.Zero, pidl, bindCtx, ref iidShellFolder, out var folderPtr);
        Console.Error.WriteLine($"ProbeStage=SHBindToObjectEnd Hr=0x{hr:X8}, FolderPtr=0x{folderPtr:X}.");
        if (hr < 0 || folderPtr == nint.Zero)
        {
            ThrowIfFailed(hr, "SHBindToObject");
        }

        return folderPtr;
    }

    private static void ThrowIfFailed(int hr, string operation)
    {
        if (hr >= 0)
        {
            return;
        }

        throw new ShellProbeException($"{operation} failed: 0x{hr:X8}.", operation, hr);
    }

    private static string FriendlySpecificHandlerMessage(string code)
    {
        return code switch
        {
            "CoCreateHandlerNoIUnknown" =>
                "The selected shell extension could not be created as an IUnknown COM object. It may not support isolated probing in this process environment.",
            "CoCreateHandlerFailed" =>
                "The selected shell extension could not be created in the analysis helper process.",
            "ShellExtInitNotSupported" or "IContextMenuNotSupported" =>
                "The shell extension cannot be initialized as a single isolated handler. It may depend on Explorer's full context menu aggregation environment.",
            "ShellExtInitInitializeFailed" or "SpecificHandlerBackgroundInitializationFailed" =>
                "The shell extension was created, but it rejected the sample target during isolated initialization.",
            "QueryContextMenuFailed" =>
                "The shell extension was initialized, but failed while building its isolated context menu.",
            "SpecificHandlerReturnedNoItems" =>
                "The shell extension initialized successfully but did not insert any menu items for the sample target.",
            _ =>
                "The selected shell extension could not be probed as an isolated handler."
        };
    }

    private static string BuildStageDiagnostic(string stage, int hr)
        => $"Stage={stage}; HRESULT=0x{hr:X8}";

    private static IReadOnlyList<ContextMenuDeepAnalysisMenuItem> EnumerateMenu(nint hmenu, IContextMenu contextMenu)
    {
        var count = NativeMethods.GetMenuItemCount(hmenu);
        var result = new List<ContextMenuDeepAnalysisMenuItem>();

        for (var index = 0; index < count; index++)
        {
            var info = new MenuItemInfo
            {
                cbSize = (uint)Marshal.SizeOf<MenuItemInfo>(),
                fMask = MiimId | MiimFtype | MiimSubmenu
            };

            if (!NativeMethods.GetMenuItemInfo(hmenu, (uint)index, true, ref info))
            {
                continue;
            }

            var isSeparator = (info.fType & MftSeparator) == MftSeparator;
            var rawText = GetMenuText(hmenu, index);
            var displayText = MenuTextSanitizer.CleanForDisplay(rawText);
            var commandOffset = info.wID >= IdCmdFirst ? (int)(info.wID - IdCmdFirst) : -1;
            var canonicalVerb = commandOffset >= 0 ? GetCommandString(contextMenu, (uint)commandOffset, GcsVerbW) : null;
            var helpText = commandOffset >= 0 ? GetCommandString(contextMenu, (uint)commandOffset, GcsHelpTextW) : null;
            var children = info.hSubMenu != nint.Zero
                ? EnumerateMenu(info.hSubMenu, contextMenu)
                : [];

            result.Add(new ContextMenuDeepAnalysisMenuItem
            {
                RawText = string.IsNullOrWhiteSpace(rawText) ? null : rawText,
                Text = string.IsNullOrWhiteSpace(displayText) ? null : displayText,
                CanonicalVerb = string.IsNullOrWhiteSpace(canonicalVerb) ? null : canonicalVerb,
                HelpText = string.IsNullOrWhiteSpace(helpText) ? null : helpText,
                CommandOffset = commandOffset,
                IsSeparator = isSeparator,
                IsSubmenu = info.hSubMenu != nint.Zero,
                Children = children
            });
        }

        return result;
    }

    private static string? GetMenuText(nint hmenu, int index)
    {
        var length = NativeMethods.GetMenuString(hmenu, (uint)index, null, 0, 0x00000400);
        if (length <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetMenuString(hmenu, (uint)index, builder, builder.Capacity, 0x00000400);
        return builder.ToString();
    }

    private static class MenuTextSanitizer
    {
        public static string? CleanForDisplay(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }

            const char placeholder = '\uE000';
            return raw
                .Replace("&&", placeholder.ToString(), StringComparison.Ordinal)
                .Replace("&", string.Empty, StringComparison.Ordinal)
                .Replace(placeholder.ToString(), "&", StringComparison.Ordinal);
        }
    }

    private static string? GetCommandString(IContextMenu contextMenu, uint commandOffset, uint flags)
    {
        var builder = new StringBuilder(512);
        var hr = contextMenu.GetCommandString(new UIntPtr(commandOffset), flags, nint.Zero, builder, builder.Capacity);
        return hr < 0 ? null : builder.ToString();
    }

    private static async Task WriteResultAsync(
        ContextMenuDeepAnalysisResult result,
        string? resultPath,
        Encoding encoding)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            try
            {
                var resultDirectory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrWhiteSpace(resultDirectory))
                {
                    Directory.CreateDirectory(resultDirectory);
                }

                await File.WriteAllTextAsync(resultPath, json, encoding);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ProbeHostResultFileWriteFailed: ResultPath={resultPath}, Exception={ex}");
            }
        }

        await Console.Out.WriteAsync(json);
    }

    private static ContextMenuDeepAnalysisResult WithDiagnostics(ContextMenuDeepAnalysisResult result)
    {
        var handlerArchitecture = ResolveHandlerArchitecture(new ContextMenuDeepAnalysisRequest
        {
            HandlerClsid = result.HandlerClsid,
            HandlerFilePath = result.HandlerFilePath
        });

        return result with
        {
            ProbeHostProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            Is64BitProcess = Environment.Is64BitProcess,
            HandlerFilePath = handlerArchitecture.FilePath ?? result.HandlerFilePath,
            HandlerFileExists = handlerArchitecture.Exists,
            HandlerFileMachineType = handlerArchitecture.MachineType,
            ArchitectureCompatibility = handlerArchitecture.Compatibility
        };
    }

    private static HandlerArchitectureInfo ResolveHandlerArchitecture(ContextMenuDeepAnalysisRequest request)
    {
        var filePath = request.HandlerFilePath;
        filePath = NormalizeComServerPath(filePath);
        if ((string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            && Guid.TryParse(request.HandlerClsid, out var clsid))
        {
            filePath = TryReadComServerPath(clsid);
            filePath = NormalizeComServerPath(filePath);
        }

        var exists = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
        var machineType = exists ? ReadPeMachineType(filePath!) : "unknown";
        var compatibility = GetArchitectureCompatibility(RuntimeInformation.ProcessArchitecture, machineType);
        return new HandlerArchitectureInfo(filePath, exists, machineType, compatibility);
    }

    private static string? TryReadComServerPath(Guid clsid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var clsidText = clsid.ToString("B");
        foreach (var valuePath in new[]
                 {
                     $@"CLSID\{clsidText}\InprocServer32",
                     $@"CLSID\{clsidText}\LocalServer32"
                 })
        {
            try
            {
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(valuePath);
                if (key?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? NormalizeComServerPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.StartsWith('"'))
        {
            var closingQuoteIndex = expanded.IndexOf('"', 1);
            if (closingQuoteIndex > 1)
            {
                return expanded[1..closingQuoteIndex];
            }
        }

        var dllIndex = expanded.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
        if (dllIndex >= 0)
        {
            return expanded[..(dllIndex + 4)];
        }

        var exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            return expanded[..(exeIndex + 4)];
        }

        return expanded;
    }

    private static string ReadPeMachineType(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40)
            {
                return "unknown";
            }

            stream.Position = 0x3C;
            var peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset <= 0 || peHeaderOffset + 6 > stream.Length)
            {
                return "unknown";
            }

            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return "unknown";
            }

            var machine = reader.ReadUInt16();
            return machine switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                0xAA64 => "arm64",
                0xA641 => "arm64ec",
                0x01C4 => "arm",
                _ => $"unknown(0x{machine:X4})"
            };
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetArchitectureCompatibility(Architecture processArchitecture, string machineType)
    {
        if (string.IsNullOrWhiteSpace(machineType) || machineType.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown";
        }

        return processArchitecture switch
        {
            Architecture.X64 when machineType is "x64" or "arm64ec" => "Compatible",
            Architecture.X86 when machineType == "x86" => "Compatible",
            Architecture.Arm64 when machineType is "arm64" or "arm64ec" => "Compatible",
            Architecture.Arm when machineType == "arm" => "Compatible",
            _ => "Mismatch"
        };
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string argumentName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static ContextMenuDeepAnalysisResult Fail(
        string code,
        string message,
        ContextMenuDeepAnalysisRequest request,
        string? samplePath = null,
        string? diagnosticDetails = null)
    {
        return Fail(code, message, request.DisplayName, request.HandlerClsid, request.HandlerFilePath, samplePath ?? request.SamplePath) with
        {
            OperationId = request.OperationId,
            ProbeMode = request.ProbeMode,
            IsSpecificHandlerResult = false,
            IsWholeContextMenuResult = false,
            SpecificHandlerFailedButWholeContextAvailable = request.ProbeMode == ContextMenuDeepAnalysisProbeMode.SpecificHandler,
            SpecificHandlerFailureCode = request.ProbeMode == ContextMenuDeepAnalysisProbeMode.SpecificHandler ? code : null,
            SpecificHandlerFailureMessage = request.ProbeMode == ContextMenuDeepAnalysisProbeMode.SpecificHandler ? message : null,
            DiagnosticDetails = diagnosticDetails
        };
    }

    private static ContextMenuDeepAnalysisResult Fail(
        string code,
        string message,
        string? displayName = null,
        string? handlerClsid = null,
        string? handlerFilePath = null,
        string? samplePath = null)
    {
        return new ContextMenuDeepAnalysisResult
        {
            Success = false,
            ErrorCode = code,
            Message = message,
            DisplayName = displayName,
            HandlerClsid = handlerClsid,
            HandlerFilePath = handlerFilePath,
            SamplePath = samplePath,
            HandlerFileExists = !string.IsNullOrWhiteSpace(handlerFilePath) && File.Exists(handlerFilePath)
        };
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(nint pici);

        [PreserveSig]
        int GetCommandString(
            UIntPtr idCmd,
            uint uType,
            nint pReserved,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
            int cchMax);
    }

    [ComImport]
    [Guid("000214E8-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellExtInit
    {
        [PreserveSig]
        int Initialize(nint pidlFolder, nint pDataObj, nint hKeyProgID);
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            nint hwnd,
            nint pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten,
            out nint ppidl,
            ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(nint hwnd, uint grfFlags, out nint ppenumIDList);

        [PreserveSig]
        int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);

        [PreserveSig]
        int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);

        [PreserveSig]
        int CompareIDs(nint lParam, nint pidl1, nint pidl2);

        [PreserveSig]
        int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, nint apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(
            nint hwndOwner,
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] apidl,
            ref Guid riid,
            nint rgfReserved,
            out nint ppv);

        [PreserveSig]
        int GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName);

        [PreserveSig]
        int SetNameOf(
            nint hwnd,
            nint pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags,
            out nint ppidlOut);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StrRet
    {
        public uint uType;
        public nint data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nuint dwItemData;
        public string? dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    private sealed class SampleContext : IDisposable
    {
        private readonly string? _rootToDelete;

        public SampleContext(string path, bool background, string? rootToDelete, bool createFile = false, bool createDirectory = false)
        {
            Path = path;
            Background = background;
            _rootToDelete = rootToDelete;

            if (createFile)
            {
                File.WriteAllText(path, "ContextMenuMgr shell extension probe sample.", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (createDirectory)
            {
                Directory.CreateDirectory(path);
            }
        }

        public string Path { get; }

        public bool Background { get; }

        public void Dispose()
        {
            if (string.IsNullOrWhiteSpace(_rootToDelete))
            {
                return;
            }

            try
            {
                Directory.Delete(_rootToDelete, true);
            }
            catch
            {
            }
        }
    }

    private sealed class ContextMenuLease : IDisposable
    {
        private readonly object? _contextMenuObject;
        private readonly object? _folderObject;
        private nint _contextMenuPtr;
        private nint _folderPtr;
        private nint _absolutePidl;
        private bool _disposed;

        public ContextMenuLease(
            IContextMenu contextMenu,
            object? contextMenuObject,
            object? folderObject,
            nint contextMenuPtr,
            nint folderPtr,
            nint absolutePidl)
        {
            ContextMenu = contextMenu;
            _contextMenuObject = contextMenuObject;
            _folderObject = folderObject;
            _contextMenuPtr = contextMenuPtr;
            _folderPtr = folderPtr;
            _absolutePidl = absolutePidl;
        }

        public IContextMenu ContextMenu { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_contextMenuObject is not null)
            {
                Marshal.FinalReleaseComObject(_contextMenuObject);
            }

            if (_folderObject is not null)
            {
                Marshal.FinalReleaseComObject(_folderObject);
            }

            if (_contextMenuPtr != nint.Zero)
            {
                Marshal.Release(_contextMenuPtr);
                _contextMenuPtr = nint.Zero;
            }

            if (_folderPtr != nint.Zero)
            {
                Marshal.Release(_folderPtr);
                _folderPtr = nint.Zero;
            }

            if (_absolutePidl != nint.Zero)
            {
                NativeMethods.CoTaskMemFree(_absolutePidl);
                _absolutePidl = nint.Zero;
            }
        }
    }

    private sealed class DataObjectLease : IDisposable
    {
        private readonly object? _folderObject;
        private nint _pidlFolder;
        private nint _absolutePidl;
        private nint _parentFolderPtr;
        private nint _dataObjectPtr;
        private bool _disposed;

        public DataObjectLease(
            nint pidlFolder,
            nint absolutePidl,
            nint parentFolderPtr,
            nint dataObjectPtr,
            object? folderObject,
            nint childPidl)
        {
            _pidlFolder = pidlFolder;
            _absolutePidl = absolutePidl;
            _parentFolderPtr = parentFolderPtr;
            _dataObjectPtr = dataObjectPtr;
            _folderObject = folderObject;
            ChildPidl = childPidl;
        }

        public nint PidlFolder => _pidlFolder;

        public nint DataObjectPtr => _dataObjectPtr;

        public nint ChildPidl { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_dataObjectPtr != nint.Zero)
            {
                Marshal.Release(_dataObjectPtr);
                _dataObjectPtr = nint.Zero;
            }

            if (_folderObject is not null)
            {
                Marshal.FinalReleaseComObject(_folderObject);
            }

            if (_parentFolderPtr != nint.Zero)
            {
                Marshal.Release(_parentFolderPtr);
                _parentFolderPtr = nint.Zero;
            }

            var pidlFolderSharesAbsolutePidl = _pidlFolder != nint.Zero && _pidlFolder == _absolutePidl;
            if (_absolutePidl != nint.Zero)
            {
                NativeMethods.CoTaskMemFree(_absolutePidl);
                _absolutePidl = nint.Zero;
            }

            if (_pidlFolder != nint.Zero && !pidlFolderSharesAbsolutePidl)
            {
                NativeMethods.CoTaskMemFree(_pidlFolder);
                _pidlFolder = nint.Zero;
            }
        }
    }

    private sealed class ShellProbeException : Exception
    {
        public ShellProbeException(string message, string operation, int hr)
            : base(message)
        {
            Operation = operation.Replace(".", string.Empty, StringComparison.Ordinal);
            Hr = hr;
        }

        public string Operation { get; }

        public int Hr { get; }
    }

    private sealed record HandlerArchitectureInfo(
        string? FilePath,
        bool Exists,
        string MachineType,
        string Compatibility);

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHParseDisplayName(
            string pszName,
            nint pbc,
            out nint ppidl,
            uint sfgaoIn,
            out uint psfgaoOut);

        [DllImport("shell32.dll")]
        internal static extern int SHBindToParent(
            nint pidl,
            ref Guid riid,
            out nint ppv,
            out nint ppidlLast);

        [DllImport("shell32.dll")]
        internal static extern int SHBindToObject(
            nint psf,
            nint pidl,
            nint pbc,
            ref Guid riid,
            out nint ppv);

        [DllImport("ole32.dll")]
        internal static extern void CoTaskMemFree(nint pv);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyMenu(nint hMenu);

        [DllImport("user32.dll")]
        internal static extern int GetMenuItemCount(nint hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetMenuItemInfo(nint hMenu, uint item, bool fByPosition, ref MenuItemInfo lpmii);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetMenuString(nint hMenu, uint uIDItem, StringBuilder? lpString, int cchMax, uint flags);

        [DllImport("ole32.dll")]
        internal static extern int CoCreateInstance(
            ref Guid rclsid,
            nint pUnkOuter,
            uint dwClsContext,
            ref Guid riid,
            out nint ppv);
    }
}
