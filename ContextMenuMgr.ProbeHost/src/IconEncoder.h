#pragma once

#include <optional>
#include <string>
#include <windows.h>

std::optional<std::string> TryEncodeHBitmapAsPngBase64(HBITMAP bitmap);
