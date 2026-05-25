#pragma once

#include <string>

void Diagnostic(const std::string& message);
std::string FormatHResult(long hr);
std::string FormatWin32Error(unsigned long error);
std::string CurrentProcessArchitecture();
std::string CurrentOSArchitecture();

