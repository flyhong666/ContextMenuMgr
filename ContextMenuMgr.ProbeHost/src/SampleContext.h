#pragma once

#include "Protocol.h"

#include <optional>
#include <string>

class SampleContext
{
public:
    SampleContext(std::wstring path, bool background, std::optional<std::wstring> rootToDelete);
    SampleContext(const SampleContext&) = delete;
    SampleContext& operator=(const SampleContext&) = delete;
    SampleContext(SampleContext&& other) noexcept;
    SampleContext& operator=(SampleContext&& other) noexcept;
    ~SampleContext();

    const std::wstring& Path() const noexcept;
    bool IsBackground() const noexcept;
    std::string PathUtf8() const;

private:
    std::wstring path_;
    bool background_ = false;
    std::optional<std::wstring> rootToDelete_;
};

std::optional<SampleContext> CreateSampleContext(const ProbeRequest& request);

