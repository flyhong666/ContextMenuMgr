#pragma once

#include <nlohmann/json.hpp>

#include <optional>
#include <string>
#include <vector>

using json = nlohmann::json;

enum class ContextMenuCategory
{
    File = 0,
    Folder = 1,
    DesktopBackground = 2,
    DirectoryBackground = 3,
    AllFileSystemObjects = 4,
    Directory = 5,
    Drive = 6,
    Library = 7,
    Computer = 8,
    RecycleBin = 9,
    Unknown = -1
};

enum class ContextMenuEntryKind
{
    ShellVerb = 0,
    ShellExtension = 1,
    Unknown = -1
};

enum class ProbeMode
{
    SpecificHandler = 0,
    WholeContextMenu = 1
};

struct ProbeRequest
{
    std::string operationId;
    std::string itemId;
    std::string displayName;
    ContextMenuCategory category = ContextMenuCategory::Unknown;
    ContextMenuEntryKind entryKind = ContextMenuEntryKind::Unknown;
    std::optional<std::string> handlerClsid;
    std::optional<std::string> handlerFilePath;
    std::optional<std::string> registryPath;
    std::optional<std::string> backendRegistryPath;
    std::optional<std::string> commandText;
    bool includeExtendedVerbs = false;
    std::optional<std::string> samplePath;
    ProbeMode probeMode = ProbeMode::SpecificHandler;
};

struct ProbeMenuItem
{
    std::optional<std::string> rawText;
    std::optional<std::string> text;
    std::optional<std::string> canonicalVerb;
    std::optional<std::string> helpText;
    int commandOffset = 0;
    bool isSeparator = false;
    bool isSubmenu = false;
    std::vector<ProbeMenuItem> children;
};

struct ProbeResult
{
    std::string operationId;
    bool success = false;
    std::optional<std::string> errorCode;
    std::optional<std::string> message;
    std::optional<std::string> displayName;
    std::optional<std::string> handlerClsid;
    std::optional<std::string> handlerFilePath;
    std::optional<std::string> samplePath;
    std::optional<std::string> probeHostProcessArchitecture;
    std::optional<std::string> osArchitecture;
    bool is64BitProcess = false;
    bool handlerFileExists = false;
    std::optional<std::string> handlerFileMachineType;
    std::optional<std::string> handlerMachineType;
    std::optional<std::string> handlerMachineRawValue;
    std::optional<std::string> selectedProbeHostArchitecture;
    std::optional<std::string> selectedProbeHostPath;
    std::optional<std::string> actualProbeHostMachineType;
    std::optional<std::string> architectureSelectionReason;
    std::optional<std::string> frontendProcessArchitecture;
    std::optional<std::string> architectureCompatibility;
    std::optional<std::string> diagnosticDetails;
    ProbeMode probeMode = ProbeMode::SpecificHandler;
    bool isSpecificHandlerResult = false;
    bool isWholeContextMenuResult = false;
    bool specificHandlerFailedButWholeContextAvailable = false;
    std::optional<std::string> specificHandlerFailureCode;
    std::optional<std::string> specificHandlerFailureMessage;
    std::vector<ProbeMenuItem> items;
};

ProbeRequest ParseRequest(const json& document);
json ToJson(const ProbeResult& result);
ProbeResult FailureResult(const ProbeRequest& request, const std::string& code, const std::string& message, const std::optional<std::string>& samplePath = std::nullopt, const std::optional<std::string>& diagnostics = std::nullopt);
ProbeResult FailureResult(const std::string& code, const std::string& message);
int ProbeModeValue(ProbeMode mode);
std::string ProbeModeName(ProbeMode mode);
std::string CategoryName(ContextMenuCategory category);
std::string EntryKindName(ContextMenuEntryKind entryKind);

