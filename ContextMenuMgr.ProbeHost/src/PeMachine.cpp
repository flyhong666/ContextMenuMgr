#include "PeMachine.h"

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <vector>
#include <windows.h>

namespace
{
std::wstring Trim(const std::wstring& value)
{
    const auto first = value.find_first_not_of(L" \t\r\n");
    if (first == std::wstring::npos)
    {
        return {};
    }

    const auto last = value.find_last_not_of(L" \t\r\n");
    return value.substr(first, last - first + 1);
}

std::wstring ToLower(std::wstring value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t ch) { return static_cast<wchar_t>(towlower(ch)); });
    return value;
}

std::optional<std::wstring> ReadRegistryDefault(HKEY root, const std::wstring& subKey)
{
    HKEY key = nullptr;
    if (RegOpenKeyExW(root, subKey.c_str(), 0, KEY_READ, &key) != ERROR_SUCCESS)
    {
        return std::nullopt;
    }

    DWORD type = 0;
    DWORD bytes = 0;
    const auto sizeResult = RegQueryValueExW(key, nullptr, nullptr, &type, nullptr, &bytes);
    if (sizeResult != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ) || bytes == 0)
    {
        RegCloseKey(key);
        return std::nullopt;
    }

    std::wstring value(bytes / sizeof(wchar_t), L'\0');
    const auto readResult = RegQueryValueExW(key, nullptr, nullptr, &type, reinterpret_cast<LPBYTE>(value.data()), &bytes);
    RegCloseKey(key);
    if (readResult != ERROR_SUCCESS)
    {
        return std::nullopt;
    }

    while (!value.empty() && value.back() == L'\0')
    {
        value.pop_back();
    }

    return value;
}

std::optional<std::wstring> ExpandEnvironment(const std::wstring& value)
{
    const DWORD required = ExpandEnvironmentStringsW(value.c_str(), nullptr, 0);
    if (required == 0)
    {
        return value;
    }

    std::wstring expanded(required, L'\0');
    const DWORD written = ExpandEnvironmentStringsW(value.c_str(), expanded.data(), required);
    if (written == 0 || written > required)
    {
        return value;
    }

    while (!expanded.empty() && expanded.back() == L'\0')
    {
        expanded.pop_back();
    }

    return expanded;
}

std::string MachineName(unsigned short machine)
{
    switch (machine)
    {
    case IMAGE_FILE_MACHINE_I386:
        return "x86";
    case IMAGE_FILE_MACHINE_AMD64:
        return "x64";
    case IMAGE_FILE_MACHINE_ARM64:
        return "arm64";
    case 0xA641:
        return "arm64ec";
    case IMAGE_FILE_MACHINE_ARMNT:
        return "arm";
    default:
        {
            std::ostringstream stream;
            stream << "unknown(0x" << std::uppercase << std::hex << std::setw(4) << std::setfill('0') << machine << ")";
            return stream.str();
        }
    }
}
}

std::optional<std::wstring> NormalizeComServerPath(const std::optional<std::wstring>& value)
{
    if (!value || value->empty())
    {
        return std::nullopt;
    }

    auto expanded = Trim(ExpandEnvironment(*value).value_or(*value));
    if (expanded.empty())
    {
        return std::nullopt;
    }

    if (expanded.front() == L'"')
    {
        const auto closing = expanded.find(L'"', 1);
        if (closing != std::wstring::npos && closing > 1)
        {
            return expanded.substr(1, closing - 1);
        }
    }

    const auto lower = ToLower(expanded);
    for (const auto* extension : { L".dll", L".exe" })
    {
        const auto index = lower.find(extension);
        if (index != std::wstring::npos)
        {
            return expanded.substr(0, index + wcslen(extension));
        }
    }

    return expanded;
}

std::optional<std::wstring> ResolveComServerPathFromClsid(const std::wstring& clsidText)
{
    for (const auto& subKey : {
             std::wstring(L"CLSID\\") + clsidText + L"\\InprocServer32",
             std::wstring(L"CLSID\\") + clsidText + L"\\LocalServer32" })
    {
        auto value = NormalizeComServerPath(ReadRegistryDefault(HKEY_CLASSES_ROOT, subKey));
        if (value && !value->empty())
        {
            return value;
        }
    }

    return std::nullopt;
}

PeMachineInfo ReadPeMachineInfo(const std::optional<std::wstring>& filePath)
{
    PeMachineInfo info{};
    if (!filePath || filePath->empty() || !std::filesystem::exists(*filePath))
    {
        return info;
    }

    info.exists = true;
    std::ifstream stream(*filePath, std::ios::binary);
    if (!stream)
    {
        return info;
    }

    stream.seekg(0, std::ios::end);
    const auto length = stream.tellg();
    if (length < 0x40)
    {
        return info;
    }

    stream.seekg(0x3C, std::ios::beg);
    int peOffset = 0;
    stream.read(reinterpret_cast<char*>(&peOffset), sizeof(peOffset));
    if (peOffset <= 0 || peOffset + 6 > length)
    {
        return info;
    }

    stream.seekg(peOffset, std::ios::beg);
    unsigned int signature = 0;
    stream.read(reinterpret_cast<char*>(&signature), sizeof(signature));
    if (signature != 0x00004550)
    {
        return info;
    }

    unsigned short machine = 0;
    stream.read(reinterpret_cast<char*>(&machine), sizeof(machine));
    std::ostringstream raw;
    raw << "0x" << std::uppercase << std::hex << std::setw(4) << std::setfill('0') << machine;
    info.rawValue = raw.str();
    info.machineType = MachineName(machine);
    return info;
}

std::string ArchitectureCompatibility(const std::string& processArchitecture, const std::string& machineType)
{
    if (machineType.empty() || machineType.rfind("unknown", 0) == 0)
    {
        return "Unknown";
    }

    if ((processArchitecture == "X64" && (machineType == "x64" || machineType == "arm64ec")) ||
        (processArchitecture == "X86" && machineType == "x86") ||
        (processArchitecture == "Arm64" && (machineType == "arm64" || machineType == "arm64ec")) ||
        (processArchitecture == "Arm" && machineType == "arm"))
    {
        return "Compatible";
    }

    return "Mismatch";
}

