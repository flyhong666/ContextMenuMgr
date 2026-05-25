#include "ShellProbe.h"

#include "Diagnostics.h"
#include "MenuEnumerator.h"
#include "PeMachine.h"
#include "SampleContext.h"
#include "WideString.h"

#include <filesystem>
#include <memory>
#include <optional>
#include <shlobj_core.h>
#include <shobjidl_core.h>
#include <sstream>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

namespace
{
constexpr UINT CmfNormal = 0;
constexpr UINT CmfExtendedVerbs = 0x00000100;
constexpr UINT IdCmdFirst = 1;
constexpr UINT IdCmdLast = 0x7FFF;

struct PidlDeleter
{
    void operator()(ITEMIDLIST* pidl) const noexcept
    {
        if (pidl != nullptr)
        {
            CoTaskMemFree(pidl);
        }
    }
};

using UniquePidl = std::unique_ptr<ITEMIDLIST, PidlDeleter>;

struct DataObjectContext
{
    UniquePidl absolutePidl;
    UniquePidl folderPidl;
    ComPtr<IShellFolder> parentFolder;
    ComPtr<IDataObject> dataObject;
    PCUITEMID_CHILD childPidl = nullptr;
};

struct ContextMenuContext
{
    UniquePidl absolutePidl;
    ComPtr<IShellFolder> folder;
    ComPtr<IContextMenu> contextMenu;
    PCUITEMID_CHILD childPidl = nullptr;
};

struct HandlerArchitecture
{
    std::optional<std::wstring> filePath;
    PeMachineInfo machine;
    std::string compatibility = "Unknown";
};

std::string OwnMachineType()
{
#if defined(_M_IX86)
    return "x86";
#elif defined(_M_X64)
    return "x64";
#elif defined(_M_ARM64)
    return "arm64";
#else
    return "unknown";
#endif
}

std::string BoolText(bool value)
{
    return value ? "true" : "false";
}

std::string StageDiagnostic(const std::string& stage, HRESULT hr)
{
    return "Stage=" + stage + "; HRESULT=" + FormatHResult(hr);
}

std::optional<std::wstring> ResolveHandlerPath(const ProbeRequest& request)
{
    auto path = NormalizeComServerPath(request.handlerFilePath ? std::optional<std::wstring>(Utf8ToWide(*request.handlerFilePath)) : std::nullopt);
    if (path && std::filesystem::exists(*path))
    {
        return path;
    }

    if (request.handlerClsid && !request.handlerClsid->empty())
    {
        return ResolveComServerPathFromClsid(Utf8ToWide(*request.handlerClsid));
    }

    return path;
}

HandlerArchitecture ResolveHandlerArchitecture(const ProbeRequest& request)
{
    HandlerArchitecture architecture;
    architecture.filePath = ResolveHandlerPath(request);
    architecture.machine = ReadPeMachineInfo(architecture.filePath);
    architecture.compatibility = ArchitectureCompatibility(CurrentProcessArchitecture(), architecture.machine.machineType);
    return architecture;
}

void EnrichArchitecture(ProbeResult& result, const HandlerArchitecture& handlerArchitecture)
{
    result.probeHostProcessArchitecture = CurrentProcessArchitecture();
    result.osArchitecture = CurrentOSArchitecture();
    result.is64BitProcess = sizeof(void*) == 8;
    result.handlerFileExists = handlerArchitecture.machine.exists;
    result.handlerFileMachineType = handlerArchitecture.machine.machineType;
    result.handlerMachineType = handlerArchitecture.machine.machineType;
    if (!handlerArchitecture.machine.rawValue.empty())
    {
        result.handlerMachineRawValue = handlerArchitecture.machine.rawValue;
    }
    result.actualProbeHostMachineType = OwnMachineType();
    result.architectureCompatibility = handlerArchitecture.compatibility;
    if (handlerArchitecture.filePath)
    {
        result.handlerFilePath = WideToUtf8(*handlerArchitecture.filePath);
    }
}

ProbeResult FailureWithArchitecture(const ProbeRequest& request, const std::string& code, const std::string& message, const HandlerArchitecture& handlerArchitecture, const std::optional<std::string>& samplePath = std::nullopt, const std::optional<std::string>& diagnostics = std::nullopt)
{
    auto result = FailureResult(request, code, message, samplePath, diagnostics);
    EnrichArchitecture(result, handlerArchitecture);
    return result;
}

ProbeResult SuccessResult(const ProbeRequest& request, const SampleContext& sample, const HandlerArchitecture& handlerArchitecture, std::vector<ProbeMenuItem> items)
{
    ProbeResult result;
    result.operationId = request.operationId;
    result.success = true;
    result.displayName = request.displayName;
    result.handlerClsid = request.handlerClsid;
    result.handlerFilePath = request.handlerFilePath;
    result.samplePath = sample.PathUtf8();
    result.probeMode = request.probeMode;
    result.isSpecificHandlerResult = request.probeMode == ProbeMode::SpecificHandler;
    result.isWholeContextMenuResult = request.probeMode == ProbeMode::WholeContextMenu;
    result.items = std::move(items);
    EnrichArchitecture(result, handlerArchitecture);
    return result;
}

HRESULT ParseDisplayName(const std::wstring& path, UniquePidl& pidl)
{
    PIDLIST_ABSOLUTE raw = nullptr;
    SFGAOF attributes = 0;
    const HRESULT hr = SHParseDisplayName(path.c_str(), nullptr, &raw, 0, &attributes);
    if (SUCCEEDED(hr))
    {
        pidl.reset(reinterpret_cast<ITEMIDLIST*>(raw));
    }
    return hr;
}

std::optional<std::wstring> ParentDirectory(const std::wstring& path)
{
    std::filesystem::path fsPath(path);
    if (!fsPath.has_parent_path())
    {
        return std::nullopt;
    }

    return fsPath.parent_path().wstring();
}

HRESULT CreateInitializationContext(const SampleContext& sample, DataObjectContext& context)
{
    HRESULT hr = ParseDisplayName(sample.Path(), context.absolutePidl);
    if (FAILED(hr))
    {
        return hr;
    }

    if (sample.IsBackground())
    {
        context.folderPidl.reset(reinterpret_cast<ITEMIDLIST*>(ILCloneFull(context.absolutePidl.get())));
        return context.folderPidl ? S_OK : E_OUTOFMEMORY;
    }

    ComPtr<IShellFolder> parentFolder;
    PCUITEMID_CHILD child = nullptr;
    hr = SHBindToParent(context.absolutePidl.get(), IID_PPV_ARGS(&parentFolder), &child);
    if (FAILED(hr))
    {
        return hr;
    }

    ComPtr<IDataObject> dataObject;
    PCUITEMID_CHILD children[] = { child };
    hr = parentFolder->GetUIObjectOf(
        nullptr,
        1,
        children,
        IID_IDataObject,
        nullptr,
        reinterpret_cast<void**>(dataObject.GetAddressOf()));
    if (FAILED(hr))
    {
        return hr;
    }

    if (auto parent = ParentDirectory(sample.Path()))
    {
        hr = ParseDisplayName(*parent, context.folderPidl);
        if (FAILED(hr))
        {
            return hr;
        }
    }

    context.parentFolder = parentFolder;
    context.dataObject = dataObject;
    context.childPidl = child;
    return S_OK;
}

HRESULT CreateWholeContextMenu(const SampleContext& sample, ContextMenuContext& context)
{
    HRESULT hr = ParseDisplayName(sample.Path(), context.absolutePidl);
    if (FAILED(hr))
    {
        return hr;
    }

    if (sample.IsBackground())
    {
        ComPtr<IShellFolder> folder;
        hr = SHBindToObject(nullptr, context.absolutePidl.get(), nullptr, IID_PPV_ARGS(&folder));
        if (FAILED(hr))
        {
            return hr;
        }

        ComPtr<IContextMenu> contextMenu;
        hr = folder->CreateViewObject(nullptr, IID_PPV_ARGS(&contextMenu));
        if (FAILED(hr))
        {
            return hr;
        }

        context.folder = folder;
        context.contextMenu = contextMenu;
        return S_OK;
    }

    ComPtr<IShellFolder> parentFolder;
    PCUITEMID_CHILD child = nullptr;
    hr = SHBindToParent(context.absolutePidl.get(), IID_PPV_ARGS(&parentFolder), &child);
    if (FAILED(hr))
    {
        return hr;
    }

    ComPtr<IContextMenu> contextMenu;
    PCUITEMID_CHILD children[] = { child };
    hr = parentFolder->GetUIObjectOf(
        nullptr,
        1,
        children,
        IID_IContextMenu,
        nullptr,
        reinterpret_cast<void**>(contextMenu.GetAddressOf()));
    if (FAILED(hr))
    {
        return hr;
    }

    context.folder = parentFolder;
    context.contextMenu = contextMenu;
    context.childPidl = child;
    return S_OK;
}

class MenuHandle
{
public:
    MenuHandle() : value_(CreatePopupMenu()) {}
    ~MenuHandle()
    {
        if (value_ != nullptr)
        {
            DestroyMenu(value_);
        }
    }

    HMENU get() const noexcept { return value_; }

private:
    HMENU value_ = nullptr;
};

ProbeResult ProbeSpecificHandler(const ProbeRequest& request, const HandlerArchitecture& handlerArchitecture)
{
    Diagnostic("ProbeStage=CreateSample");
    auto sample = CreateSampleContext(request);
    if (!sample)
    {
        return FailureWithArchitecture(request, "UnsupportedCategory", "Probing is not supported for this category.", handlerArchitecture);
    }

    try
    {
        Diagnostic("ProbeStage=SpecificHandler");
        CLSID clsid{};
        const auto clsidText = Utf8ToWide(request.handlerClsid.value_or(std::string{}));
        HRESULT hr = CLSIDFromString(clsidText.c_str(), &clsid);
        if (FAILED(hr))
        {
            return FailureWithArchitecture(request, "InvalidHandlerClsid", "Handler CLSID is missing or invalid.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("CLSIDFromString", hr));
        }

        ComPtr<IUnknown> handler;
        hr = CoCreateInstance(clsid, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&handler));
        if (FAILED(hr) || handler == nullptr)
        {
            const auto code = hr == E_NOINTERFACE ? "CoCreateHandlerNoIUnknown" : "CoCreateHandlerFailed";
            return FailureWithArchitecture(request, code, "The selected shell extension handler could not be created.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("CoCreateInstance(IUnknown)", hr));
        }

        ComPtr<IShellExtInit> shellExtInit;
        hr = handler.As(&shellExtInit);
        if (FAILED(hr) || shellExtInit == nullptr)
        {
            return FailureWithArchitecture(request, "ShellExtInitNotSupported", "The selected shell extension does not support IShellExtInit.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("QueryInterface(IShellExtInit)", hr));
        }

        DataObjectContext initContext;
        hr = CreateInitializationContext(*sample, initContext);
        if (FAILED(hr))
        {
            return FailureWithArchitecture(request, "CreateInitializationContextFailed", "Failed to create the shell initialization context.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("CreateInitializationContext", hr));
        }

        hr = shellExtInit->Initialize(initContext.folderPidl.get(), initContext.dataObject.Get(), nullptr);
        if (FAILED(hr))
        {
            const auto code = sample->IsBackground() ? "SpecificHandlerBackgroundInitializationFailed" : "ShellExtInitInitializeFailed";
            return FailureWithArchitecture(request, code, "The selected shell extension could not be initialized for the sample target.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("IShellExtInit.Initialize", hr));
        }

        ComPtr<IContextMenu> contextMenu;
        hr = handler.As(&contextMenu);
        if (FAILED(hr) || contextMenu == nullptr)
        {
            return FailureWithArchitecture(request, "IContextMenuNotSupported", "The selected shell extension does not support IContextMenu.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("QueryInterface(IContextMenu)", hr));
        }

        MenuHandle menu;
        if (menu.get() == nullptr)
        {
            return FailureWithArchitecture(request, "CreatePopupMenuFailed", "CreatePopupMenu failed.", handlerArchitecture, sample->PathUtf8(), "Stage=CreatePopupMenu; LastError=" + FormatWin32Error(GetLastError()));
        }

        const UINT flags = request.includeExtendedVerbs ? CmfNormal | CmfExtendedVerbs : CmfNormal;
        hr = contextMenu->QueryContextMenu(menu.get(), 0, IdCmdFirst, IdCmdLast, flags);
        if (FAILED(hr))
        {
            return FailureWithArchitecture(request, "QueryContextMenuFailed", "IContextMenu.QueryContextMenu failed.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("IContextMenu.QueryContextMenu", hr));
        }

        Diagnostic("ProbeStage=EnumerateMenu");
        auto items = EnumerateMenu(menu.get(), contextMenu.Get());
        const auto hasNonSeparator = std::any_of(items.begin(), items.end(), [](const ProbeMenuItem& item) { return !item.isSeparator; });
        if (!hasNonSeparator)
        {
            return FailureWithArchitecture(request, "SpecificHandlerReturnedNoItems", "The selected shell extension returned no visible menu items.", handlerArchitecture, sample->PathUtf8(), "Stage=EnumerateMenu; ItemCount=0");
        }

        auto result = SuccessResult(request, *sample, handlerArchitecture, std::move(items));
        result.isSpecificHandlerResult = true;
        result.isWholeContextMenuResult = false;
        return result;
    }
    catch (const std::exception& ex)
    {
        return FailureWithArchitecture(request, "ProbeFailed", ex.what(), handlerArchitecture, sample->PathUtf8());
    }
}

ProbeResult ProbeWholeContextMenu(const ProbeRequest& request, const HandlerArchitecture& handlerArchitecture)
{
    Diagnostic("ProbeStage=CreateSample");
    auto sample = CreateSampleContext(request);
    if (!sample)
    {
        return FailureWithArchitecture(request, "UnsupportedCategory", "Probing is not supported for this category.", handlerArchitecture);
    }

    try
    {
        Diagnostic("ProbeStage=WholeContextMenu");
        ContextMenuContext context;
        HRESULT hr = CreateWholeContextMenu(*sample, context);
        if (FAILED(hr))
        {
            return FailureWithArchitecture(request, "CreateContextMenuFailed", "Failed to create the shell context menu for the sample target.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("CreateContextMenu", hr));
        }

        MenuHandle menu;
        if (menu.get() == nullptr)
        {
            return FailureWithArchitecture(request, "CreatePopupMenuFailed", "CreatePopupMenu failed.", handlerArchitecture, sample->PathUtf8(), "Stage=CreatePopupMenu; LastError=" + FormatWin32Error(GetLastError()));
        }

        const UINT flags = request.includeExtendedVerbs ? CmfNormal | CmfExtendedVerbs : CmfNormal;
        hr = context.contextMenu->QueryContextMenu(menu.get(), 0, IdCmdFirst, IdCmdLast, flags);
        if (FAILED(hr))
        {
            return FailureWithArchitecture(request, "QueryContextMenuFailed", "IContextMenu.QueryContextMenu failed.", handlerArchitecture, sample->PathUtf8(), StageDiagnostic("IContextMenu.QueryContextMenu", hr));
        }

        Diagnostic("ProbeStage=EnumerateMenu");
        auto items = EnumerateMenu(menu.get(), context.contextMenu.Get());
        auto result = SuccessResult(request, *sample, handlerArchitecture, std::move(items));
        result.isSpecificHandlerResult = false;
        result.isWholeContextMenuResult = true;
        return result;
    }
    catch (const std::exception& ex)
    {
        return FailureWithArchitecture(request, "ProbeFailed", ex.what(), handlerArchitecture, sample->PathUtf8());
    }
}
}

ProbeResult RunProbe(const ProbeRequest& request)
{
    Diagnostic("ProbeStage=ValidateRequest");
    if (request.entryKind != ContextMenuEntryKind::ShellExtension)
    {
        return FailureResult(request, "UnsupportedEntryKind", "Only Shell Extension entries can be probed.");
    }

    if (!request.handlerClsid || request.handlerClsid->empty())
    {
        return FailureResult(request, "InvalidHandlerClsid", "Handler CLSID is missing or invalid.");
    }

    const auto handlerArchitecture = ResolveHandlerArchitecture(request);
    Diagnostic(
        "ProbeArchitecture: ProcessArchitecture=" + CurrentProcessArchitecture() +
        ", OSArchitecture=" + CurrentOSArchitecture() +
        ", HandlerFileExists=" + BoolText(handlerArchitecture.machine.exists) +
        ", HandlerMachineType=" + handlerArchitecture.machine.machineType +
        ", Compatibility=" + handlerArchitecture.compatibility + ".");

    if (handlerArchitecture.compatibility == "Mismatch")
    {
        return FailureWithArchitecture(
            request,
            "ArchitectureMismatch",
            "The shell extension appears to use a different process architecture from the analysis helper. Runtime probing is skipped to avoid crashing.",
            handlerArchitecture);
    }

    if (request.probeMode == ProbeMode::WholeContextMenu)
    {
        return ProbeWholeContextMenu(request, handlerArchitecture);
    }

    return ProbeSpecificHandler(request, handlerArchitecture);
}
