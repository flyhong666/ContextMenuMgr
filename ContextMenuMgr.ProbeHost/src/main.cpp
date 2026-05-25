#include "Diagnostics.h"
#include "Protocol.h"
#include "ShellProbe.h"

#include <fstream>
#include <iostream>
#include <filesystem>
#include <optional>
#include <string>
#include <objbase.h>
#include <windows.h>

namespace
{
std::optional<std::wstring> ArgumentValue(int argc, wchar_t* argv[], const wchar_t* name)
{
    for (int index = 1; index < argc - 1; ++index)
    {
        if (_wcsicmp(argv[index], name) == 0)
        {
            return argv[index + 1];
        }
    }

    return std::nullopt;
}

std::string ReadUtf8File(const std::wstring& path)
{
    std::ifstream stream(path, std::ios::binary);
    if (!stream)
    {
        throw std::runtime_error("Could not open request JSON.");
    }

    return std::string(std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>());
}

bool WriteUtf8File(const std::wstring& path, const std::string& text)
{
    std::filesystem::path filePath(path);
    std::error_code error;
    if (filePath.has_parent_path())
    {
        std::filesystem::create_directories(filePath.parent_path(), error);
        if (error)
        {
            Diagnostic("ProbeHostResultFileWriteFailed: CreateDirectory failed.");
            return false;
        }
    }

    std::ofstream stream(filePath, std::ios::binary | std::ios::trunc);
    if (!stream)
    {
        Diagnostic("ProbeHostResultFileWriteFailed: Could not open result JSON.");
        return false;
    }

    stream.write(text.data(), static_cast<std::streamsize>(text.size()));
    return stream.good();
}

void EnrichProcessDiagnostics(ProbeResult& result)
{
    result.probeHostProcessArchitecture = CurrentProcessArchitecture();
    result.osArchitecture = CurrentOSArchitecture();
    result.is64BitProcess = sizeof(void*) == 8;
#if defined(_M_IX86)
    result.actualProbeHostMachineType = "x86";
#elif defined(_M_X64)
    result.actualProbeHostMachineType = "x64";
#elif defined(_M_ARM64)
    result.actualProbeHostMachineType = "arm64";
#else
    result.actualProbeHostMachineType = "unknown";
#endif
}

bool WriteResult(const ProbeResult& result, const std::optional<std::wstring>& resultPath)
{
    const auto text = ToJson(result).dump();
    if (resultPath.has_value() && !resultPath->empty())
    {
        if (WriteUtf8File(*resultPath, text))
        {
            return true;
        }
    }

    std::cout << text;
    return static_cast<bool>(std::cout);
}
}

int wmain(int argc, wchar_t* argv[])
{
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    Diagnostic("ProbeHostStart: ProcessArchitecture=" + CurrentProcessArchitecture() + ", OSArchitecture=" + CurrentOSArchitecture() + ".");

    const auto requestPath = ArgumentValue(argc, argv, L"--request");
    const auto resultPath = ArgumentValue(argc, argv, L"--result");
    std::optional<ProbeRequest> request;

    try
    {
        Diagnostic("ProbeStage=ValidateRequest");
        if (!requestPath.has_value() || requestPath->empty() || !resultPath.has_value() || resultPath->empty())
        {
            auto result = FailureResult("InvalidProtocol", "Expected command line: ContextMenuMgr.ProbeHost.exe --request <request.json> --result <result.json>");
            EnrichProcessDiagnostics(result);
            return WriteResult(result, resultPath) ? 2 : 1;
        }

        const auto requestJson = ReadUtf8File(*requestPath);
        request = ParseRequest(json::parse(requestJson));
        Diagnostic("ProbeHostRequestParsed: OperationId=" + request->operationId + ", ItemId=" + request->itemId + ", Category=" + CategoryName(request->category) + ", EntryKind=" + EntryKindName(request->entryKind) + ", ProbeMode=" + ProbeModeName(request->probeMode) + ".");

        const HRESULT comHr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        if (FAILED(comHr))
        {
            auto result = FailureResult(*request, "ComInitializeFailed", "COM initialization failed.", std::nullopt, "Stage=CoInitializeEx; HRESULT=" + FormatHResult(comHr));
            EnrichProcessDiagnostics(result);
            return WriteResult(result, resultPath) ? 2 : 1;
        }

        ProbeResult result;
        try
        {
            result = RunProbe(*request);
            EnrichProcessDiagnostics(result);
        }
        catch (const std::exception& ex)
        {
            result = FailureResult(*request, "UnhandledException", ex.what());
            EnrichProcessDiagnostics(result);
        }

        CoUninitialize();
        Diagnostic("ProbeStage=WriteResult");
        if (!WriteResult(result, resultPath))
        {
            return 1;
        }

        return result.success ? 0 : 2;
    }
    catch (const std::exception& ex)
    {
        Diagnostic(std::string("ProbeHostUnhandledException: ") + ex.what());
        ProbeResult result = request.has_value()
            ? FailureResult(*request, "UnhandledException", ex.what())
            : FailureResult("InvalidRequest", ex.what());
        EnrichProcessDiagnostics(result);
        return WriteResult(result, resultPath) ? 2 : 1;
    }
}
