#include "Protocol.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <stdexcept>

using json = nlohmann::json;

namespace
{
std::string Lower(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    return value;
}

std::optional<std::string> OptionalString(const json& document, const char* name)
{
    if (!document.contains(name) || document.at(name).is_null())
    {
        return std::nullopt;
    }

    if (document.at(name).is_string())
    {
        return document.at(name).get<std::string>();
    }

    return document.at(name).dump();
}

std::string StringOrEmpty(const json& document, const char* name)
{
    auto value = OptionalString(document, name);
    return value.value_or(std::string{});
}

bool BoolOrFalse(const json& document, const char* name)
{
    if (!document.contains(name) || document.at(name).is_null())
    {
        return false;
    }

    if (document.at(name).is_boolean())
    {
        return document.at(name).get<bool>();
    }

    if (document.at(name).is_number_integer())
    {
        return document.at(name).get<int>() != 0;
    }

    if (document.at(name).is_string())
    {
        const auto text = Lower(document.at(name).get<std::string>());
        return text == "true" || text == "1" || text == "yes";
    }

    return false;
}

ContextMenuCategory ParseCategory(const json& document)
{
    if (!document.contains("category") || document.at("category").is_null())
    {
        return ContextMenuCategory::Unknown;
    }

    const auto& value = document.at("category");
    if (value.is_number_integer())
    {
        switch (value.get<int>())
        {
        case 0: return ContextMenuCategory::File;
        case 1: return ContextMenuCategory::Folder;
        case 2: return ContextMenuCategory::DesktopBackground;
        case 3: return ContextMenuCategory::DirectoryBackground;
        case 4: return ContextMenuCategory::AllFileSystemObjects;
        case 5: return ContextMenuCategory::Directory;
        case 6: return ContextMenuCategory::Drive;
        case 7: return ContextMenuCategory::Library;
        case 8: return ContextMenuCategory::Computer;
        case 9: return ContextMenuCategory::RecycleBin;
        default: return ContextMenuCategory::Unknown;
        }
    }

    if (value.is_string())
    {
        const auto text = Lower(value.get<std::string>());
        if (text == "file") return ContextMenuCategory::File;
        if (text == "folder") return ContextMenuCategory::Folder;
        if (text == "desktopbackground") return ContextMenuCategory::DesktopBackground;
        if (text == "directorybackground") return ContextMenuCategory::DirectoryBackground;
        if (text == "allfilesystemobjects") return ContextMenuCategory::AllFileSystemObjects;
        if (text == "directory") return ContextMenuCategory::Directory;
        if (text == "drive") return ContextMenuCategory::Drive;
        if (text == "library") return ContextMenuCategory::Library;
        if (text == "computer") return ContextMenuCategory::Computer;
        if (text == "recyclebin") return ContextMenuCategory::RecycleBin;
    }

    return ContextMenuCategory::Unknown;
}

ContextMenuEntryKind ParseEntryKind(const json& document)
{
    if (!document.contains("entryKind") || document.at("entryKind").is_null())
    {
        return ContextMenuEntryKind::Unknown;
    }

    const auto& value = document.at("entryKind");
    if (value.is_number_integer())
    {
        switch (value.get<int>())
        {
        case 0: return ContextMenuEntryKind::ShellVerb;
        case 1: return ContextMenuEntryKind::ShellExtension;
        default: return ContextMenuEntryKind::Unknown;
        }
    }

    if (value.is_string())
    {
        const auto text = Lower(value.get<std::string>());
        if (text == "shellverb") return ContextMenuEntryKind::ShellVerb;
        if (text == "shellextension") return ContextMenuEntryKind::ShellExtension;
    }

    return ContextMenuEntryKind::Unknown;
}

ProbeMode ParseProbeMode(const json& document)
{
    if (!document.contains("probeMode") || document.at("probeMode").is_null())
    {
        return ProbeMode::SpecificHandler;
    }

    const auto& value = document.at("probeMode");
    if (value.is_number_integer())
    {
        return value.get<int>() == 1 ? ProbeMode::WholeContextMenu : ProbeMode::SpecificHandler;
    }

    if (value.is_string())
    {
        const auto text = Lower(value.get<std::string>());
        if (text == "wholecontextmenu")
        {
            return ProbeMode::WholeContextMenu;
        }
    }

    return ProbeMode::SpecificHandler;
}

void PutOptional(json& document, const char* name, const std::optional<std::string>& value)
{
    if (value.has_value())
    {
        document[name] = *value;
    }
    else
    {
        document[name] = nullptr;
    }
}

json MenuItemToJson(const ProbeMenuItem& item)
{
    json document;
    PutOptional(document, "rawText", item.rawText);
    PutOptional(document, "text", item.text);
    PutOptional(document, "canonicalVerb", item.canonicalVerb);
    PutOptional(document, "helpText", item.helpText);
    PutOptional(document, "iconPngBase64", item.iconPngBase64);
    document["commandOffset"] = item.commandOffset;
    document["isSeparator"] = item.isSeparator;
    document["isSubmenu"] = item.isSubmenu;
    document["children"] = json::array();
    for (const auto& child : item.children)
    {
        document["children"].push_back(MenuItemToJson(child));
    }

    return document;
}
}

ProbeRequest ParseRequestJson(const std::string& text)
{
    const auto document = json::parse(text);
    if (!document.is_object())
    {
        throw std::runtime_error("Request JSON root must be an object.");
    }

    ProbeRequest request;
    request.operationId = StringOrEmpty(document, "operationId");
    request.itemId = StringOrEmpty(document, "itemId");
    request.displayName = StringOrEmpty(document, "displayName");
    request.category = ParseCategory(document);
    request.entryKind = ParseEntryKind(document);
    request.handlerClsid = OptionalString(document, "handlerClsid");
    request.handlerFilePath = OptionalString(document, "handlerFilePath");
    request.registryPath = OptionalString(document, "registryPath");
    request.backendRegistryPath = OptionalString(document, "backendRegistryPath");
    request.commandText = OptionalString(document, "commandText");
    request.includeExtendedVerbs = BoolOrFalse(document, "includeExtendedVerbs");
    request.samplePath = OptionalString(document, "samplePath");
    request.probeMode = ParseProbeMode(document);
    return request;
}

std::string SerializeResultJson(const ProbeResult& result)
{
    json document;
    document["operationId"] = result.operationId;
    document["success"] = result.success;
    PutOptional(document, "errorCode", result.errorCode);
    PutOptional(document, "message", result.message);
    PutOptional(document, "displayName", result.displayName);
    PutOptional(document, "handlerClsid", result.handlerClsid);
    PutOptional(document, "handlerFilePath", result.handlerFilePath);
    PutOptional(document, "samplePath", result.samplePath);
    PutOptional(document, "probeHostProcessArchitecture", result.probeHostProcessArchitecture);
    PutOptional(document, "osArchitecture", result.osArchitecture);
    document["is64BitProcess"] = result.is64BitProcess;
    document["handlerFileExists"] = result.handlerFileExists;
    PutOptional(document, "handlerFileMachineType", result.handlerFileMachineType);
    PutOptional(document, "handlerMachineType", result.handlerMachineType);
    PutOptional(document, "handlerMachineRawValue", result.handlerMachineRawValue);
    PutOptional(document, "selectedProbeHostArchitecture", result.selectedProbeHostArchitecture);
    PutOptional(document, "selectedProbeHostPath", result.selectedProbeHostPath);
    PutOptional(document, "actualProbeHostMachineType", result.actualProbeHostMachineType);
    PutOptional(document, "architectureSelectionReason", result.architectureSelectionReason);
    PutOptional(document, "frontendProcessArchitecture", result.frontendProcessArchitecture);
    PutOptional(document, "architectureCompatibility", result.architectureCompatibility);
    PutOptional(document, "diagnosticDetails", result.diagnosticDetails);
    document["probeMode"] = ProbeModeValue(result.probeMode);
    document["isSpecificHandlerResult"] = result.isSpecificHandlerResult;
    document["isWholeContextMenuResult"] = result.isWholeContextMenuResult;
    document["specificHandlerFailedButWholeContextAvailable"] = result.specificHandlerFailedButWholeContextAvailable;
    PutOptional(document, "specificHandlerFailureCode", result.specificHandlerFailureCode);
    PutOptional(document, "specificHandlerFailureMessage", result.specificHandlerFailureMessage);
    document["items"] = json::array();
    for (const auto& item : result.items)
    {
        document["items"].push_back(MenuItemToJson(item));
    }

    return document.dump();
}

ProbeResult FailureResult(const ProbeRequest& request, const std::string& code, const std::string& message, const std::optional<std::string>& samplePath, const std::optional<std::string>& diagnostics)
{
    ProbeResult result;
    result.operationId = request.operationId;
    result.success = false;
    result.errorCode = code;
    result.message = message;
    result.displayName = request.displayName;
    result.handlerClsid = request.handlerClsid;
    result.handlerFilePath = request.handlerFilePath;
    result.samplePath = samplePath.has_value() ? samplePath : request.samplePath;
    result.probeMode = request.probeMode;
    result.isSpecificHandlerResult = false;
    result.isWholeContextMenuResult = false;
    result.specificHandlerFailedButWholeContextAvailable = request.probeMode == ProbeMode::SpecificHandler;
    if (request.probeMode == ProbeMode::SpecificHandler)
    {
        result.specificHandlerFailureCode = code;
        result.specificHandlerFailureMessage = message;
    }
    result.diagnosticDetails = diagnostics;
    return result;
}

ProbeResult FailureResult(const std::string& code, const std::string& message)
{
    ProbeResult result;
    result.operationId = "00000000-0000-0000-0000-000000000000";
    result.success = false;
    result.errorCode = code;
    result.message = message;
    return result;
}

int ProbeModeValue(ProbeMode mode)
{
    return mode == ProbeMode::WholeContextMenu ? 1 : 0;
}

std::string ProbeModeName(ProbeMode mode)
{
    return mode == ProbeMode::WholeContextMenu ? "WholeContextMenu" : "SpecificHandler";
}

std::string CategoryName(ContextMenuCategory category)
{
    switch (category)
    {
    case ContextMenuCategory::File: return "File";
    case ContextMenuCategory::Folder: return "Folder";
    case ContextMenuCategory::DesktopBackground: return "DesktopBackground";
    case ContextMenuCategory::DirectoryBackground: return "DirectoryBackground";
    case ContextMenuCategory::AllFileSystemObjects: return "AllFileSystemObjects";
    case ContextMenuCategory::Directory: return "Directory";
    case ContextMenuCategory::Drive: return "Drive";
    case ContextMenuCategory::Library: return "Library";
    case ContextMenuCategory::Computer: return "Computer";
    case ContextMenuCategory::RecycleBin: return "RecycleBin";
    default: return "Unknown";
    }
}

std::string EntryKindName(ContextMenuEntryKind entryKind)
{
    switch (entryKind)
    {
    case ContextMenuEntryKind::ShellVerb: return "ShellVerb";
    case ContextMenuEntryKind::ShellExtension: return "ShellExtension";
    default: return "Unknown";
    }
}
