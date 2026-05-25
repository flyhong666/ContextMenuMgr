#include "WideString.h"

#include <stdexcept>
#include <windows.h>

std::wstring Utf8ToWide(const std::string& value)
{
    if (value.empty())
    {
        return {};
    }

    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (length <= 0)
    {
        throw std::runtime_error("Invalid UTF-8 input.");
    }

    std::wstring result(static_cast<size_t>(length), L'\0');
    const int written = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length);
    if (written != length)
    {
        throw std::runtime_error("UTF-8 to UTF-16 conversion failed.");
    }

    return result;
}

std::string WideToUtf8(const std::wstring& value)
{
    if (value.empty())
    {
        return {};
    }

    const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (length <= 0)
    {
        throw std::runtime_error("Invalid UTF-16 input.");
    }

    std::string result(static_cast<size_t>(length), '\0');
    const int written = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
    if (written != length)
    {
        throw std::runtime_error("UTF-16 to UTF-8 conversion failed.");
    }

    return result;
}

