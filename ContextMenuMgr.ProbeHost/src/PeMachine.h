#pragma once

#include <optional>
#include <string>

struct PeMachineInfo
{
    bool exists = false;
    std::string machineType = "unknown";
    std::string rawValue;
};

std::optional<std::wstring> NormalizeComServerPath(const std::optional<std::wstring>& value);
std::optional<std::wstring> ResolveComServerPathFromClsid(const std::wstring& clsidText);
PeMachineInfo ReadPeMachineInfo(const std::optional<std::wstring>& filePath);
std::string ArchitectureCompatibility(const std::string& processArchitecture, const std::string& machineType);

