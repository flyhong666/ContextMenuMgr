#include "IconEncoder.h"

#include <cstddef>
#include <cstdint>
#include <objidl.h>
#include <span>
#include <wincodec.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

namespace
{
constexpr ULONGLONG MaxPngBytes = 256ULL * 1024ULL;

class GlobalMemoryLock
{
public:
    explicit GlobalMemoryLock(HGLOBAL memory) : memory_(memory), value_(GlobalLock(memory)) {}
    ~GlobalMemoryLock()
    {
        if (value_ != nullptr)
        {
            GlobalUnlock(memory_);
        }
    }

    void* get() const noexcept { return value_; }

private:
    HGLOBAL memory_ = nullptr;
    void* value_ = nullptr;
};

std::string Base64Encode(std::span<const std::byte> data)
{
    static constexpr char Alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    std::string result;
    result.reserve(((data.size() + 2) / 3) * 4);

    size_t index = 0;
    while (index + 3 <= data.size())
    {
        const uint32_t value =
            (static_cast<uint32_t>(std::to_integer<unsigned char>(data[index])) << 16) |
            (static_cast<uint32_t>(std::to_integer<unsigned char>(data[index + 1])) << 8) |
            static_cast<uint32_t>(std::to_integer<unsigned char>(data[index + 2]));

        result.push_back(Alphabet[(value >> 18) & 0x3F]);
        result.push_back(Alphabet[(value >> 12) & 0x3F]);
        result.push_back(Alphabet[(value >> 6) & 0x3F]);
        result.push_back(Alphabet[value & 0x3F]);
        index += 3;
    }

    const size_t remaining = data.size() - index;
    if (remaining == 1)
    {
        const uint32_t value = static_cast<uint32_t>(std::to_integer<unsigned char>(data[index])) << 16;
        result.push_back(Alphabet[(value >> 18) & 0x3F]);
        result.push_back(Alphabet[(value >> 12) & 0x3F]);
        result.push_back('=');
        result.push_back('=');
    }
    else if (remaining == 2)
    {
        const uint32_t value =
            (static_cast<uint32_t>(std::to_integer<unsigned char>(data[index])) << 16) |
            (static_cast<uint32_t>(std::to_integer<unsigned char>(data[index + 1])) << 8);
        result.push_back(Alphabet[(value >> 18) & 0x3F]);
        result.push_back(Alphabet[(value >> 12) & 0x3F]);
        result.push_back(Alphabet[(value >> 6) & 0x3F]);
        result.push_back('=');
    }

    return result;
}
}

std::optional<std::string> TryEncodeHBitmapAsPngBase64(HBITMAP bitmap)
{
    if (bitmap == nullptr)
    {
        return std::nullopt;
    }

    try
    {
        ComPtr<IWICImagingFactory> factory;
        HRESULT hr = CoCreateInstance(
            CLSID_WICImagingFactory,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&factory));
        if (FAILED(hr) || factory == nullptr)
        {
            return std::nullopt;
        }

        ComPtr<IWICBitmap> wicBitmap;
        hr = factory->CreateBitmapFromHBITMAP(bitmap, nullptr, WICBitmapUseAlpha, &wicBitmap);
        if (FAILED(hr) || wicBitmap == nullptr)
        {
            return std::nullopt;
        }

        UINT width = 0;
        UINT height = 0;
        hr = wicBitmap->GetSize(&width, &height);
        if (FAILED(hr) || width == 0 || height == 0 || width > 256 || height > 256)
        {
            return std::nullopt;
        }

        ComPtr<IStream> stream;
        hr = CreateStreamOnHGlobal(nullptr, TRUE, &stream);
        if (FAILED(hr) || stream == nullptr)
        {
            return std::nullopt;
        }

        ComPtr<IWICBitmapEncoder> encoder;
        hr = factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder);
        if (FAILED(hr) || encoder == nullptr)
        {
            return std::nullopt;
        }

        hr = encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache);
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        ComPtr<IWICBitmapFrameEncode> frame;
        hr = encoder->CreateNewFrame(&frame, nullptr);
        if (FAILED(hr) || frame == nullptr)
        {
            return std::nullopt;
        }

        hr = frame->Initialize(nullptr);
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        hr = frame->SetSize(width, height);
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        WICPixelFormatGUID pixelFormat = GUID_WICPixelFormat32bppBGRA;
        hr = frame->SetPixelFormat(&pixelFormat);
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        hr = frame->WriteSource(wicBitmap.Get(), nullptr);
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        hr = frame->Commit();
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        hr = encoder->Commit();
        if (FAILED(hr))
        {
            return std::nullopt;
        }

        STATSTG stat{};
        hr = stream->Stat(&stat, STATFLAG_NONAME);
        if (FAILED(hr) || stat.cbSize.QuadPart <= 0 || static_cast<ULONGLONG>(stat.cbSize.QuadPart) > MaxPngBytes)
        {
            return std::nullopt;
        }

        HGLOBAL memory = nullptr;
        hr = GetHGlobalFromStream(stream.Get(), &memory);
        if (FAILED(hr) || memory == nullptr)
        {
            return std::nullopt;
        }

        GlobalMemoryLock locked(memory);
        if (locked.get() == nullptr)
        {
            return std::nullopt;
        }

        const auto size = static_cast<size_t>(stat.cbSize.QuadPart);
        const auto* bytes = static_cast<const std::byte*>(locked.get());
        auto base64 = Base64Encode(std::span<const std::byte>(bytes, size));
        return base64.empty() ? std::nullopt : std::optional<std::string>(std::move(base64));
    }
    catch (...)
    {
        return std::nullopt;
    }
}
