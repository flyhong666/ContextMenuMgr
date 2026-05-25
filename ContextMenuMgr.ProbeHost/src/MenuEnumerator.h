#pragma once

#include "Protocol.h"

#include <shobjidl_core.h>
#include <windows.h>

std::vector<ProbeMenuItem> EnumerateMenu(HMENU menu, IContextMenu* contextMenu);
std::string CleanMenuText(const std::wstring& rawText);

