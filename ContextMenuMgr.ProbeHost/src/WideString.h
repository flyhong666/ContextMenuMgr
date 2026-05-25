#pragma once

#include <string>

std::wstring Utf8ToWide(const std::string& value);
std::string WideToUtf8(const std::wstring& value);

