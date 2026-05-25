#include "Diagnostics.h"

#include <iomanip>
#include <iostream>
#include <sstream>
#include <windows.h>

void Diagnostic(const std::string& message)
{
    std::cerr << message << std::endl;
}

std::string FormatHResult(long hr)
{
    std::ostringstream stream;
    stream << "0x" << std::uppercase << std::hex << std::setw(8) << std::setfill('0') << static_cast<unsigned long>(hr);
    return stream.str();
}

std::string FormatWin32Error(unsigned long error)
{
    std::ostringstream stream;
    stream << "0x" << std::uppercase << std::hex << std::setw(8) << std::setfill('0') << error;
    return stream.str();
}

std::string CurrentProcessArchitecture()
{
#if defined(_M_IX86)
    return "X86";
#elif defined(_M_X64)
    return "X64";
#elif defined(_M_ARM64)
    return "Arm64";
#elif defined(_M_ARM)
    return "Arm";
#else
    return "Unknown";
#endif
}

std::string CurrentOSArchitecture()
{
    USHORT machine = IMAGE_FILE_MACHINE_UNKNOWN;
    USHORT nativeMachine = IMAGE_FILE_MACHINE_UNKNOWN;
    if (IsWow64Process2(GetCurrentProcess(), &machine, &nativeMachine))
    {
        switch (nativeMachine)
        {
        case IMAGE_FILE_MACHINE_I386:
            return "X86";
        case IMAGE_FILE_MACHINE_AMD64:
            return "X64";
        case IMAGE_FILE_MACHINE_ARM64:
            return "Arm64";
        case IMAGE_FILE_MACHINE_ARMNT:
            return "Arm";
        default:
            return "Unknown";
        }
    }

    SYSTEM_INFO info{};
    GetNativeSystemInfo(&info);
    switch (info.wProcessorArchitecture)
    {
    case PROCESSOR_ARCHITECTURE_INTEL:
        return "X86";
    case PROCESSOR_ARCHITECTURE_AMD64:
        return "X64";
    case PROCESSOR_ARCHITECTURE_ARM64:
        return "Arm64";
    case PROCESSOR_ARCHITECTURE_ARM:
        return "Arm";
    default:
        return "Unknown";
    }
}

