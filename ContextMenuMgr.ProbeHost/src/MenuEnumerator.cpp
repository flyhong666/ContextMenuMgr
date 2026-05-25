#include "MenuEnumerator.h"

#include "IconEncoder.h"
#include "WideString.h"

#include <string>
#include <vector>

namespace
{
constexpr UINT IdCmdFirst = 1;
constexpr UINT GcsVerbW = 4;
constexpr UINT GcsHelpTextW = 5;

std::optional<std::wstring> GetMenuText(HMENU menu, int index)
{
    const int length = GetMenuStringW(menu, static_cast<UINT>(index), nullptr, 0, MF_BYPOSITION);
    if (length <= 0)
    {
        return std::nullopt;
    }

    std::wstring text(static_cast<size_t>(length) + 1, L'\0');
    const int written = GetMenuStringW(menu, static_cast<UINT>(index), text.data(), static_cast<int>(text.size()), MF_BYPOSITION);
    if (written <= 0)
    {
        return std::nullopt;
    }

    text.resize(static_cast<size_t>(written));
    return text;
}

std::optional<std::string> GetCommandString(IContextMenu* contextMenu, UINT commandOffset, UINT flags)
{
    if (contextMenu == nullptr)
    {
        return std::nullopt;
    }

    std::wstring buffer(512, L'\0');
    const HRESULT hr = contextMenu->GetCommandString(
        static_cast<UINT_PTR>(commandOffset),
        flags,
        nullptr,
        reinterpret_cast<LPSTR>(buffer.data()),
        static_cast<UINT>(buffer.size()));
    if (FAILED(hr))
    {
        return std::nullopt;
    }

    const auto terminator = buffer.find(L'\0');
    if (terminator != std::wstring::npos)
    {
        buffer.resize(terminator);
    }

    if (buffer.empty())
    {
        return std::nullopt;
    }

    return WideToUtf8(buffer);
}

bool IsPredefinedMenuBitmap(HBITMAP bitmap)
{
    const auto value = reinterpret_cast<INT_PTR>(bitmap);
    return value == -1 || (value >= 1 && value <= 11);
}
}

std::string CleanMenuText(const std::wstring& rawText)
{
    std::wstring cleaned;
    cleaned.reserve(rawText.size());
    for (size_t index = 0; index < rawText.size(); ++index)
    {
        const wchar_t ch = rawText[index];
        if (ch == L'&')
        {
            if (index + 1 < rawText.size() && rawText[index + 1] == L'&')
            {
                cleaned.push_back(L'&');
                ++index;
            }

            continue;
        }

        cleaned.push_back(ch);
    }

    return WideToUtf8(cleaned);
}

std::vector<ProbeMenuItem> EnumerateMenu(HMENU menu, IContextMenu* contextMenu)
{
    std::vector<ProbeMenuItem> result;
    if (menu == nullptr)
    {
        return result;
    }

    const int count = GetMenuItemCount(menu);
    if (count <= 0)
    {
        return result;
    }

    result.reserve(static_cast<size_t>(count));
    for (int index = 0; index < count; ++index)
    {
        MENUITEMINFOW info{};
        info.cbSize = sizeof(info);
        info.fMask = MIIM_FTYPE | MIIM_ID | MIIM_SUBMENU | MIIM_BITMAP;
        if (!GetMenuItemInfoW(menu, static_cast<UINT>(index), TRUE, &info))
        {
            continue;
        }

        const bool separator = (info.fType & MFT_SEPARATOR) == MFT_SEPARATOR;
        const bool ownerDraw = (info.fType & MFT_OWNERDRAW) == MFT_OWNERDRAW;
        const auto rawText = GetMenuText(menu, index);
        const int commandOffset = info.wID >= IdCmdFirst ? static_cast<int>(info.wID - IdCmdFirst) : 0;

        ProbeMenuItem item;
        if (rawText.has_value())
        {
            item.rawText = WideToUtf8(*rawText);
            item.text = CleanMenuText(*rawText);
        }

        item.commandOffset = commandOffset;
        item.isSeparator = separator;
        item.isSubmenu = info.hSubMenu != nullptr;
        if (!separator && !ownerDraw && info.hbmpItem != nullptr && !IsPredefinedMenuBitmap(info.hbmpItem))
        {
            item.iconPngBase64 = TryEncodeHBitmapAsPngBase64(info.hbmpItem);
        }

        if (!separator)
        {
            item.canonicalVerb = GetCommandString(contextMenu, static_cast<UINT>(commandOffset), GcsVerbW);
            item.helpText = GetCommandString(contextMenu, static_cast<UINT>(commandOffset), GcsHelpTextW);
        }

        if (info.hSubMenu != nullptr)
        {
            item.children = EnumerateMenu(info.hSubMenu, contextMenu);
        }

        result.push_back(std::move(item));
    }

    return result;
}
