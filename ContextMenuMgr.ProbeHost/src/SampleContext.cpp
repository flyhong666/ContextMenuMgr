#include "SampleContext.h"

#include "WideString.h"

#include <filesystem>
#include <fstream>
#include <objbase.h>
#include <shlobj_core.h>

namespace
{
bool Exists(const std::wstring& path)
{
    std::error_code error;
    return std::filesystem::exists(path, error);
}

std::wstring NewGuidText()
{
    GUID guid{};
    if (FAILED(CoCreateGuid(&guid)))
    {
        return L"fallback";
    }

    wchar_t buffer[39]{};
    StringFromGUID2(guid, buffer, static_cast<int>(std::size(buffer)));
    std::wstring value(buffer);
    if (value.size() >= 2 && value.front() == L'{' && value.back() == L'}')
    {
        value = value.substr(1, value.size() - 2);
    }
    return value;
}

std::wstring TempRoot()
{
    wchar_t temp[MAX_PATH]{};
    const DWORD length = GetTempPathW(static_cast<DWORD>(std::size(temp)), temp);
    const std::filesystem::path base = length > 0 ? std::filesystem::path(temp) : std::filesystem::temp_directory_path();
    return (base / L"ContextMenuMgr.ProbeHost" / NewGuidText()).wstring();
}

std::optional<std::wstring> DesktopPath()
{
    PWSTR path = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_Desktop, KF_FLAG_DEFAULT, nullptr, &path)))
    {
        return std::nullopt;
    }

    std::wstring result(path);
    CoTaskMemFree(path);
    return result;
}

bool IsBackgroundCategory(ContextMenuCategory category)
{
    return category == ContextMenuCategory::DirectoryBackground || category == ContextMenuCategory::DesktopBackground;
}
}

SampleContext::SampleContext(std::wstring path, bool background, std::optional<std::wstring> rootToDelete)
    : path_(std::move(path)), background_(background), rootToDelete_(std::move(rootToDelete))
{
}

SampleContext::SampleContext(SampleContext&& other) noexcept
    : path_(std::move(other.path_)), background_(other.background_), rootToDelete_(std::move(other.rootToDelete_))
{
    other.rootToDelete_.reset();
}

SampleContext& SampleContext::operator=(SampleContext&& other) noexcept
{
    if (this != &other)
    {
        path_ = std::move(other.path_);
        background_ = other.background_;
        rootToDelete_ = std::move(other.rootToDelete_);
        other.rootToDelete_.reset();
    }

    return *this;
}

SampleContext::~SampleContext()
{
    if (!rootToDelete_.has_value())
    {
        return;
    }

    std::error_code error;
    std::filesystem::remove_all(*rootToDelete_, error);
}

const std::wstring& SampleContext::Path() const noexcept
{
    return path_;
}

bool SampleContext::IsBackground() const noexcept
{
    return background_;
}

std::string SampleContext::PathUtf8() const
{
    return WideToUtf8(path_);
}

std::optional<SampleContext> CreateSampleContext(const ProbeRequest& request)
{
    if (request.samplePath.has_value() && !request.samplePath->empty())
    {
        const auto path = Utf8ToWide(*request.samplePath);
        if (Exists(path))
        {
            return SampleContext(path, IsBackgroundCategory(request.category), std::nullopt);
        }
    }

    const auto root = TempRoot();
    std::error_code error;
    std::filesystem::create_directories(root, error);
    if (error)
    {
        return std::nullopt;
    }

    switch (request.category)
    {
    case ContextMenuCategory::File:
    case ContextMenuCategory::AllFileSystemObjects:
        {
            const auto path = (std::filesystem::path(root) / L"sample.txt").wstring();
            std::ofstream file(path, std::ios::binary);
            const std::string content = "ContextMenuMgr shell extension probe sample.";
            file.write(content.data(), static_cast<std::streamsize>(content.size()));
            file.close();
            return SampleContext(path, false, root);
        }
    case ContextMenuCategory::Folder:
    case ContextMenuCategory::Directory:
        {
            const auto path = (std::filesystem::path(root) / L"SampleFolder").wstring();
            std::filesystem::create_directories(path, error);
            if (error)
            {
                return std::nullopt;
            }
            return SampleContext(path, false, root);
        }
    case ContextMenuCategory::DirectoryBackground:
        return SampleContext(root, true, root);
    case ContextMenuCategory::DesktopBackground:
        if (auto desktop = DesktopPath())
        {
            return SampleContext(*desktop, true, root);
        }
        return SampleContext(root, true, root);
    default:
        std::filesystem::remove_all(root, error);
        return std::nullopt;
    }
}

