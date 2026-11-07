// ============================================================================
//  TextureEncoder.cpp  -  RGBA (8-bit or float) -> Unity texture data (+ mipmaps)
//
//  Backends:
//    * Raw formats (RGBA32, RGB565, Half, Float, RGB9e5, YUY2, etc.) -> Internal routines
//    * BC1/BC3/BC4/BC5/BC6H/BC7                                      -> ISPC Texture Compressor
//    * ETC1/ETC2/EAC/PVRTC/ASTC (LDR and HDR)                        -> PVRTexLib
//    * Crunch (DXT1/DXT5/ETC1/ETC2A)                                 -> crnlib (Unity fork)
//
//  Preprocessor Feature Toggles:
//    -DENCODER_USE_ISPC=0  -DENCODER_USE_PVRTEXLIB=0  -DENCODER_USE_CRUNCH=0
//
//  Output Layout (Unity compatible):
//    Sequential, tightly packed mip levels: mip 0, mip 1, mip 2, ...
//    Crunch produces a self-contained .crn container with embedded mip levels.
//
//  API Protocol (two-step retrieval):
//      uint size = EncodeTextureNative(..., &mipsOut, &handle, cb);
//      byte[] buf = new byte[size];
//      PickUpAndFree(buf, size, handle);
// ============================================================================
#include "pch.h"
#include "Export.h"
#include "TextureFormat.h"

#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <cmath>
#include <algorithm>
#include <vector>
#include <unordered_map>
#include <mutex>

#ifndef ENCODER_USE_ISPC
#define ENCODER_USE_ISPC 1
#endif
#ifndef ENCODER_USE_PVRTEXLIB
#define ENCODER_USE_PVRTEXLIB 1
#endif
#ifndef ENCODER_USE_CRUNCH
#define ENCODER_USE_CRUNCH 1
#endif

// Memory reclamation policy for buffers returned by crn_compress:
//   0 = Do not free (prevents heap corruption across mismatched CRT boundaries)
//   1 = Free via std::free (enable only when crnlib and this binary share the same C runtime)
#ifndef ENCODER_CRUNCH_FREE_BLOCK
#define ENCODER_CRUNCH_FREE_BLOCK 0
#endif

#if ENCODER_USE_CRUNCH
#if defined(_WIN32) && !defined(WIN32)
#define WIN32   // Required by crnlib headers on Windows platforms
#endif
#include "crunch/inc/crnlib.h"
#endif
#if ENCODER_USE_PVRTEXLIB
#include "PVRTexLib/Include/PVRTexLib.hpp"
#endif
#if ENCODER_USE_ISPC
#include "ispc/include/ispc_texcomp.h"
#endif

enum EncodeFlags : int32_t
{
    ENC_INPUT_FLOAT = 1,   // Input buffer contains RGBA32F data (default: RGBA8)
    ENC_SRGB_MIPS = 2    // Perform mipmap downsampling in linear color space (for sRGB textures)
};

namespace
{
    // ------------------------------------------------------------------------
    //  Binary & Conversion Helpers
    // ------------------------------------------------------------------------
    inline float RdF(const uint8_t* p) { float v; std::memcpy(&v, p, 4); return v; }
    inline void  W16(uint8_t* d, uint16_t v) { std::memcpy(d, &v, 2); }
    inline void  W32(uint8_t* d, uint32_t v) { std::memcpy(d, &v, 4); }
    inline void  WF(uint8_t* d, float v) { std::memcpy(d, &v, 4); }

    inline uint32_t Q(float v, uint32_t maxv)
    {
        if (!(v > 0.0f)) return 0;
        if (v >= 1.0f)   return maxv;
        return (uint32_t)(v * (float)maxv + 0.5f);
    }
    inline uint8_t  F2B(float v) { return (uint8_t)Q(v, 255); }
    inline uint16_t F2U16(float v) { return (uint16_t)Q(v, 65535); }

    uint16_t FloatToHalf(float f)
    {
        uint32_t x; std::memcpy(&x, &f, 4);
        const uint16_t sign = (uint16_t)((x >> 16) & 0x8000u);
        const uint32_t e8 = (x >> 23) & 0xFFu;
        uint32_t mant = x & 0x7FFFFFu;

        // Inf / NaN
        if (e8 == 0xFF) return (uint16_t)(sign | 0x7C00u | (mant ? 0x200u : 0u));
        int32_t exp = (int32_t)e8 - 127 + 15;

        // Overflow -> Clamp to Infinity
        if (exp >= 31) return (uint16_t)(sign | 0x7C00u);

        // Denormalized number or zero
        if (exp <= 0)
        {
            if (exp < -10) return sign;
            mant |= 0x800000u;
            const int shift = 14 - exp;
            uint32_t half = mant >> shift;
            const uint32_t rem = mant & ((1u << shift) - 1u), mid = 1u << (shift - 1);
            if (rem > mid || (rem == mid && (half & 1u))) ++half;
            return (uint16_t)(sign | half);
        }

        // Normalized half-precision float
        uint32_t half = ((uint32_t)exp << 10) | (mant >> 13);
        const uint32_t rem = mant & 0x1FFFu;
        if (rem > 0x1000u || (rem == 0x1000u && (half & 1u))) ++half;
        return (uint16_t)(sign | half);
    }

    uint32_t EncodeRGB9e5(float r, float g, float b)
    {
        const float maxv = 65408.0f; // (511 / 512) * 2^16
        auto cl = [&](float v) { return !(v > 0.0f) ? 0.0f : (v > maxv ? maxv : v); };
        r = cl(r); g = cl(g); b = cl(b);
        const float m = (std::max)(r, (std::max)(g, b));
        if (m <= 0.0f) return 0;

        int e = (int)std::floor(std::log2(m));
        int exps = (std::max)(-16, e) + 16;
        float denom = std::ldexp(1.0f, exps - 24);
        if ((int)std::floor(m / denom + 0.5f) == 512) { ++exps; denom *= 2.0f; }

        const uint32_t rm = (uint32_t)std::floor(r / denom + 0.5f);
        const uint32_t gm = (uint32_t)std::floor(g / denom + 0.5f);
        const uint32_t bm = (uint32_t)std::floor(b / denom + 0.5f);
        return rm | (gm << 9) | (bm << 18) | ((uint32_t)exps << 27);
    }

    inline size_t Ceil(size_t a, size_t b) { return (a + b - 1) / b; }

    bool AstcDimsEnc(TextureFormat f, int& bx, int& by, bool& hdr)
    {
        hdr = false;
        switch (f)
        {
        case TextureFormat::ASTC_RGB_4x4:   case TextureFormat::ASTC_RGBA_4x4:   bx = by = 4;  return true;
        case TextureFormat::ASTC_RGB_5x5:   case TextureFormat::ASTC_RGBA_5x5:   bx = by = 5;  return true;
        case TextureFormat::ASTC_RGB_6x6:   case TextureFormat::ASTC_RGBA_6x6:   bx = by = 6;  return true;
        case TextureFormat::ASTC_RGB_8x8:   case TextureFormat::ASTC_RGBA_8x8:   bx = by = 8;  return true;
        case TextureFormat::ASTC_RGB_10x10: case TextureFormat::ASTC_RGBA_10x10: bx = by = 10; return true;
        case TextureFormat::ASTC_RGB_12x12: case TextureFormat::ASTC_RGBA_12x12: bx = by = 12; return true;
        case TextureFormat::ASTC_HDR_4x4:   hdr = true; bx = by = 4;  return true;
        case TextureFormat::ASTC_HDR_5x5:   hdr = true; bx = by = 5;  return true;
        case TextureFormat::ASTC_HDR_6x6:   hdr = true; bx = by = 6;  return true;
        case TextureFormat::ASTC_HDR_8x8:   hdr = true; bx = by = 8;  return true;
        case TextureFormat::ASTC_HDR_10x10: hdr = true; bx = by = 10; return true;
        case TextureFormat::ASTC_HDR_12x12: hdr = true; bx = by = 12; return true;
        default: return false;
        }
    }

    // Encoded byte size for a single mipmap level (returns 0 if unsupported)
    size_t LevelSize(TextureFormat f, size_t w, size_t h)
    {
        const size_t px = w * h;
        switch (f)
        {
        case TextureFormat::Alpha8:
        case TextureFormat::R8:            return px;
        case TextureFormat::ARGB4444:
        case TextureFormat::RGBA4444:
        case TextureFormat::RGB565:
        case TextureFormat::R16:
        case TextureFormat::RG16:
        case TextureFormat::RHalf:
        case TextureFormat::YUY2:          return px * 2;
        case TextureFormat::RGB24:
        case TextureFormat::BGR24:         return px * 3;
        case TextureFormat::RGBA32:
        case TextureFormat::ARGB32:
        case TextureFormat::BGRA32:
        case TextureFormat::BGRA32Old:
        case TextureFormat::RGHalf:
        case TextureFormat::RFloat:
        case TextureFormat::RGB9e5Float:
        case TextureFormat::RG32:          return px * 4;
        case TextureFormat::RGB48:         return px * 6;
        case TextureFormat::RGBAHalf:
        case TextureFormat::RGFloat:
        case TextureFormat::RGBA64:        return px * 8;
        case TextureFormat::RGBFloat:      return px * 12;
        case TextureFormat::RGBAFloat:     return px * 16;

        case TextureFormat::DXT1:
        case TextureFormat::BC4:
        case TextureFormat::ETC_RGB4:
        case TextureFormat::ETC_RGB4_3DS:
        case TextureFormat::EAC_R:
        case TextureFormat::EAC_R_SIGNED:
        case TextureFormat::ETC2_RGB4:
        case TextureFormat::ETC2_RGBA1:    return Ceil(w, 4) * Ceil(h, 4) * 8;

        case TextureFormat::DXT3:
        case TextureFormat::DXT5:
        case TextureFormat::BC5:
        case TextureFormat::BC6H:
        case TextureFormat::BC7:
        case TextureFormat::EAC_RG:
        case TextureFormat::EAC_RG_SIGNED:
        case TextureFormat::ETC2_RGBA8:
        case TextureFormat::ETC_RGBA8_3DS: return Ceil(w, 4) * Ceil(h, 4) * 16;

        case TextureFormat::PVRTC_RGB2:
        case TextureFormat::PVRTC_RGBA2:
            return (std::max<size_t>)(Ceil(w, 8), 2) * (std::max<size_t>)(Ceil(h, 4), 2) * 8;
        case TextureFormat::PVRTC_RGB4:
        case TextureFormat::PVRTC_RGBA4:
            return (std::max<size_t>)(Ceil(w, 4), 2) * (std::max<size_t>)(Ceil(h, 4), 2) * 8;
        default: break;
        }
        int bx, by; bool hdr;
        if (AstcDimsEnc(f, bx, by, hdr)) return Ceil(w, bx) * Ceil(h, by) * 16;
        return 0;
    }

    inline bool IsCrunch(TextureFormat f)
    {
        return f == TextureFormat::DXT1Crunched || f == TextureFormat::DXT5Crunched ||
            f == TextureFormat::ETC_RGB4Crunched || f == TextureFormat::ETC2_RGBA8Crunched;
    }

    inline int MaxMips(int w, int h)
    {
        int m = 1, s = (std::max)(w, h);
        while (s > 1) { s >>= 1; ++m; }
        return m;
    }

    // ------------------------------------------------------------------------
    //  Pixel Source Abstraction (RGBA8 / RGBA32F -> Normalized Float RGBA)
    // ------------------------------------------------------------------------
    struct Src
    {
        const uint8_t* p;
        bool isF;

        inline void get(size_t i, float& r, float& g, float& b, float& a) const
        {
            if (isF)
            {
                const uint8_t* q = p + i * 16;
                r = RdF(q); g = RdF(q + 4); b = RdF(q + 8); a = RdF(q + 12);
            }
            else
            {
                const uint8_t* q = p + i * 4;
                const float k = 1.0f / 255.0f;
                r = q[0] * k; g = q[1] * k; b = q[2] * k; a = q[3] * k;
            }
        }
    };

    // ------------------------------------------------------------------------
    //  Mipmap Generation (Box Filtering, NPOT Compatible)
    // ------------------------------------------------------------------------
    float SrgbToLin(uint8_t v)
    {
        static const struct Lut {
            float t[256]; Lut() {
                for (int i = 0; i < 256; ++i) {
                    float c = i / 255.0f;
                    t[i] = c <= 0.04045f ? c / 12.92f : std::pow((c + 0.055f) / 1.055f, 2.4f);
                }
            }
        } lut;
        return lut.t[v];
    }

    uint8_t LinToSrgb(float l)
    {
        if (!(l > 0.0f)) return 0;
        if (l >= 1.0f)   return 255;
        float c = l <= 0.0031308f ? l * 12.92f : 1.055f * std::pow(l, 1.0f / 2.4f) - 0.055f;
        return F2B(c);
    }

    void Down8(const uint8_t* s, int sw, int sh, uint8_t* d, int dw, int dh, bool srgb)
    {
        for (int y = 0; y < dh; ++y)
        {
            const int sy0 = (int)((int64_t)y * sh / dh);
            const int sy1 = (std::max)(sy0 + 1, (int)((int64_t)(y + 1) * sh / dh));
            for (int x = 0; x < dw; ++x)
            {
                const int sx0 = (int)((int64_t)x * sw / dw);
                const int sx1 = (std::max)(sx0 + 1, (int)((int64_t)(x + 1) * sw / dw));
                const uint32_t cnt = (uint32_t)((sx1 - sx0) * (sy1 - sy0));
                uint8_t* o = d + ((size_t)y * dw + x) * 4;

                if (!srgb)
                {
                    uint32_t acc[4] = { 0, 0, 0, 0 };
                    for (int j = sy0; j < sy1; ++j)
                        for (int i = sx0; i < sx1; ++i)
                        {
                            const uint8_t* q = s + ((size_t)j * sw + i) * 4;
                            acc[0] += q[0]; acc[1] += q[1]; acc[2] += q[2]; acc[3] += q[3];
                        }
                    for (int c = 0; c < 4; ++c) o[c] = (uint8_t)((acc[c] + cnt / 2) / cnt);
                }
                else
                {
                    float acc[3] = { 0, 0, 0 };
                    uint32_t accA = 0;
                    for (int j = sy0; j < sy1; ++j)
                        for (int i = sx0; i < sx1; ++i)
                        {
                            const uint8_t* q = s + ((size_t)j * sw + i) * 4;
                            acc[0] += SrgbToLin(q[0]); acc[1] += SrgbToLin(q[1]); acc[2] += SrgbToLin(q[2]);
                            accA += q[3];
                        }
                    const float inv = 1.0f / (float)cnt;
                    o[0] = LinToSrgb(acc[0] * inv); o[1] = LinToSrgb(acc[1] * inv); o[2] = LinToSrgb(acc[2] * inv);
                    o[3] = (uint8_t)((accA + cnt / 2) / cnt); // Alpha channel is preserved in linear space
                }
            }
        }
    }

    void DownF(const uint8_t* s, int sw, int sh, uint8_t* d, int dw, int dh)
    {
        for (int y = 0; y < dh; ++y)
        {
            const int sy0 = (int)((int64_t)y * sh / dh);
            const int sy1 = (std::max)(sy0 + 1, (int)((int64_t)(y + 1) * sh / dh));
            for (int x = 0; x < dw; ++x)
            {
                const int sx0 = (int)((int64_t)x * sw / dw);
                const int sx1 = (std::max)(sx0 + 1, (int)((int64_t)(x + 1) * sw / dw));
                float acc[4] = { 0, 0, 0, 0 };
                for (int j = sy0; j < sy1; ++j)
                    for (int i = sx0; i < sx1; ++i)
                    {
                        const uint8_t* q = s + ((size_t)j * sw + i) * 16;
                        for (int c = 0; c < 4; ++c) acc[c] += RdF(q + c * 4);
                    }
                const float inv = 1.0f / (float)((sx1 - sx0) * (sy1 - sy0));
                uint8_t* o = d + ((size_t)y * dw + x) * 16;
                for (int c = 0; c < 4; ++c) WF(o + c * 4, acc[c] * inv);
            }
        }
    }

    // ------------------------------------------------------------------------
    //  1. Raw / Uncompressed Formats
    // ------------------------------------------------------------------------
    bool IsRawFormat(TextureFormat f)
    {
        switch (f)
        {
        case TextureFormat::Alpha8: case TextureFormat::ARGB4444: case TextureFormat::RGB24:
        case TextureFormat::RGBA32: case TextureFormat::ARGB32:   case TextureFormat::RGB565:
        case TextureFormat::BGR24:  case TextureFormat::R16:      case TextureFormat::RGBA4444:
        case TextureFormat::BGRA32: case TextureFormat::BGRA32Old:
        case TextureFormat::RHalf:  case TextureFormat::RGHalf:   case TextureFormat::RGBAHalf:
        case TextureFormat::RFloat: case TextureFormat::RGFloat:  case TextureFormat::RGBFloat:
        case TextureFormat::RGBAFloat: case TextureFormat::YUY2:  case TextureFormat::RGB9e5Float:
        case TextureFormat::RG16:   case TextureFormat::R8:       case TextureFormat::RG32:
        case TextureFormat::RGB48:  case TextureFormat::RGBA64:
            return true;
        default: return false;
        }
    }

    bool EncodeRaw(const Src& s, int w, int h, TextureFormat f, std::vector<uint8_t>& out)
    {
        const size_t n = (size_t)w * h;
        const size_t bytes = LevelSize(f, w, h);
        if (!bytes) return false;

        const size_t base = out.size();
        out.resize(base + bytes);
        uint8_t* d = out.data() + base;

        // Fast path for 8-bit RGBA32
        if (f == TextureFormat::RGBA32 && !s.isF)
        {
            std::memcpy(d, s.p, bytes);
            return true;
        }

        if (f == TextureFormat::YUY2)
        {
            if (w & 1) { out.resize(base); return false; } // YUY2 requires an even texture width
            for (int y = 0; y < h; ++y)
                for (int x = 0; x < w; x += 2)
                {
                    float r0, g0, b0, a0, r1, g1, b1, a1;
                    s.get((size_t)y * w + x, r0, g0, b0, a0);
                    s.get((size_t)y * w + x + 1, r1, g1, b1, a1);
                    const int R0 = F2B(r0), G0 = F2B(g0), B0 = F2B(b0);
                    const int R1 = F2B(r1), G1 = F2B(g1), B1 = F2B(b1);
                    const int Y0 = 16 + ((66 * R0 + 129 * G0 + 25 * B0 + 128) >> 8);
                    const int Y1 = 16 + ((66 * R1 + 129 * G1 + 25 * B1 + 128) >> 8);
                    const int R = (R0 + R1 + 1) >> 1, G = (G0 + G1 + 1) >> 1, B = (B0 + B1 + 1) >> 1;
                    const int U = 128 + ((-38 * R - 74 * G + 112 * B + 128) >> 8);
                    const int V = 128 + ((112 * R - 94 * G - 18 * B + 128) >> 8);
                    d[0] = (uint8_t)std::clamp(Y0, 0, 255); d[1] = (uint8_t)std::clamp(U, 0, 255);
                    d[2] = (uint8_t)std::clamp(Y1, 0, 255); d[3] = (uint8_t)std::clamp(V, 0, 255);
                    d += 4;
                }
            return true;
        }

        for (size_t i = 0; i < n; ++i)
        {
            float r, g, b, a;
            s.get(i, r, g, b, a);
            switch (f)
            {
            case TextureFormat::RGBA32:   d[0] = F2B(r); d[1] = F2B(g); d[2] = F2B(b); d[3] = F2B(a); d += 4; break;
            case TextureFormat::BGRA32:
            case TextureFormat::BGRA32Old:d[0] = F2B(b); d[1] = F2B(g); d[2] = F2B(r); d[3] = F2B(a); d += 4; break;
            case TextureFormat::ARGB32:   d[0] = F2B(a); d[1] = F2B(r); d[2] = F2B(g); d[3] = F2B(b); d += 4; break;
            case TextureFormat::RGB24:    d[0] = F2B(r); d[1] = F2B(g); d[2] = F2B(b); d += 3; break;
            case TextureFormat::BGR24:    d[0] = F2B(b); d[1] = F2B(g); d[2] = F2B(r); d += 3; break;
            case TextureFormat::Alpha8:   d[0] = F2B(a); d += 1; break;
            case TextureFormat::R8:       d[0] = F2B(r); d += 1; break;
            case TextureFormat::RG16:     d[0] = F2B(r); d[1] = F2B(g); d += 2; break;
            case TextureFormat::R16:      W16(d, F2U16(r)); d += 2; break;
            case TextureFormat::RG32:     W16(d, F2U16(r)); W16(d + 2, F2U16(g)); d += 4; break;
            case TextureFormat::RGB48:    W16(d, F2U16(r)); W16(d + 2, F2U16(g)); W16(d + 4, F2U16(b)); d += 6; break;
            case TextureFormat::RGBA64:   W16(d, F2U16(r)); W16(d + 2, F2U16(g)); W16(d + 4, F2U16(b)); W16(d + 6, F2U16(a)); d += 8; break;
            case TextureFormat::RGBA4444: W16(d, (uint16_t)((Q(r, 15) << 12) | (Q(g, 15) << 8) | (Q(b, 15) << 4) | Q(a, 15))); d += 2; break;
            case TextureFormat::ARGB4444: W16(d, (uint16_t)((Q(a, 15) << 12) | (Q(r, 15) << 8) | (Q(g, 15) << 4) | Q(b, 15))); d += 2; break;
            case TextureFormat::RGB565:   W16(d, (uint16_t)((Q(r, 31) << 11) | (Q(g, 63) << 5) | Q(b, 31))); d += 2; break;
            case TextureFormat::RHalf:    W16(d, FloatToHalf(r)); d += 2; break;
            case TextureFormat::RGHalf:   W16(d, FloatToHalf(r)); W16(d + 2, FloatToHalf(g)); d += 4; break;
            case TextureFormat::RGBAHalf: W16(d, FloatToHalf(r)); W16(d + 2, FloatToHalf(g)); W16(d + 4, FloatToHalf(b)); W16(d + 6, FloatToHalf(a)); d += 8; break;
            case TextureFormat::RFloat:   WF(d, r); d += 4; break;
            case TextureFormat::RGFloat:  WF(d, r); WF(d + 4, g); d += 8; break;
            case TextureFormat::RGBFloat: WF(d, r); WF(d + 4, g); WF(d + 8, b); d += 12; break;
            case TextureFormat::RGBAFloat:WF(d, r); WF(d + 4, g); WF(d + 8, b); WF(d + 12, a); d += 16; break;
            case TextureFormat::RGB9e5Float: W32(d, EncodeRGB9e5(r, g, b)); d += 4; break;
            default: out.resize(base); return false;
            }
        }
        return true;
    }

#if ENCODER_USE_ISPC
    // ------------------------------------------------------------------------
    //  2. Block Compression (BC1-BC7) via ISPC Texture Compressor
    //     Clamps and replicates edge pixels to maintain 4x4 block alignment.
    // ------------------------------------------------------------------------
    bool EncodeBC(const Src& s, int w, int h, TextureFormat f, int quality, std::vector<uint8_t>& out)
    {
        switch (f)
        {
        case TextureFormat::DXT1: case TextureFormat::DXT5: case TextureFormat::BC4:
        case TextureFormat::BC5:  case TextureFormat::BC6H: case TextureFormat::BC7: break;
        default: return false; // DXT3 is not natively handled by ISPC
        }

        const int pw = (w + 3) & ~3, ph = (h + 3) & ~3;
        const size_t bytes = LevelSize(f, w, h);
        const size_t base = out.size();
        out.resize(base + bytes);
        uint8_t* dst = out.data() + base;

        std::vector<uint8_t> pad;
        rgba_surface surf;
        surf.width = pw; surf.height = ph;

        if (f == TextureFormat::BC6H)
        {
            pad.resize((size_t)pw * ph * 8);
            for (int y = 0; y < ph; ++y)
            {
                const int sy = (std::min)(y, h - 1);
                for (int x = 0; x < pw; ++x)
                {
                    float r, g, b, a;
                    s.get((size_t)sy * w + (std::min)(x, w - 1), r, g, b, a);
                    uint8_t* o = pad.data() + ((size_t)y * pw + x) * 8;
                    W16(o, FloatToHalf(r)); W16(o + 2, FloatToHalf(g)); W16(o + 4, FloatToHalf(b)); W16(o + 6, 0x3C00); // 1.0 in half float
                }
            }
            surf.stride = pw * 8;
        }
        else
        {
            pad.resize((size_t)pw * ph * 4);
            for (int y = 0; y < ph; ++y)
            {
                const int sy = (std::min)(y, h - 1);
                for (int x = 0; x < pw; ++x)
                {
                    float r, g, b, a;
                    s.get((size_t)sy * w + (std::min)(x, w - 1), r, g, b, a);
                    uint8_t* o = pad.data() + ((size_t)y * pw + x) * 4;
                    o[0] = F2B(r); o[1] = F2B(g); o[2] = F2B(b); o[3] = F2B(a);
                }
            }
            surf.stride = pw * 4;
        }
        surf.ptr = pad.data();

        switch (f)
        {
        case TextureFormat::DXT1: CompressBlocksBC1(&surf, dst); break;
        case TextureFormat::DXT5: CompressBlocksBC3(&surf, dst); break;
        case TextureFormat::BC4:  CompressBlocksBC4(&surf, dst); break;
        case TextureFormat::BC5:  CompressBlocksBC5(&surf, dst); break;
        case TextureFormat::BC6H:
        {
            bc6h_enc_settings st;
            GetProfile_bc6h_basic(&st);
            CompressBlocksBC6H(&surf, dst, &st);
            break;
        }
        case TextureFormat::BC7:
        {
            bc7_enc_settings st;
            if (quality <= 1)      GetProfile_alpha_fast(&st);
            else if (quality >= 4) GetProfile_alpha_slow(&st);
            else                   GetProfile_alpha_basic(&st);
            CompressBlocksBC7(&surf, dst, &st);
            break;
        }
        default: break;
        }
        return true;
    }
#endif

#if ENCODER_USE_PVRTEXLIB
    // ------------------------------------------------------------------------
    //  3. Mobile & GPU Compression Formats (ETC / EAC / PVRTC / ASTC) via PVRTexLib
    // ------------------------------------------------------------------------
    bool MapPvrEnc(TextureFormat f, uint64_t& pf, PVRTexLibVariableType& vt, PVRTexLibCompressorQuality& q, bool& hdr)
    {
        vt = PVRTLVT_UnsignedByteNorm;
        hdr = false;
        q = PVRTLCQ_ETCNormal;
        switch (f)
        {
        case TextureFormat::ETC_RGB4:
        case TextureFormat::ETC_RGB4_3DS:     pf = PVRTLPF_ETC1;        return true;
        case TextureFormat::ETC2_RGB4:        pf = PVRTLPF_ETC2_RGB;    return true;
        case TextureFormat::ETC2_RGBA1:       pf = PVRTLPF_ETC2_RGB_A1; return true;
        case TextureFormat::ETC2_RGBA8:
        case TextureFormat::ETC_RGBA8_3DS:    pf = PVRTLPF_ETC2_RGBA;   return true;
        case TextureFormat::EAC_R:            pf = PVRTLPF_EAC_R11;     return true;
        case TextureFormat::EAC_R_SIGNED:     pf = PVRTLPF_EAC_R11;  vt = PVRTLVT_SignedIntegerNorm; return true;
        case TextureFormat::EAC_RG:           pf = PVRTLPF_EAC_RG11;    return true;
        case TextureFormat::EAC_RG_SIGNED:    pf = PVRTLPF_EAC_RG11; vt = PVRTLVT_SignedIntegerNorm; return true;
        default: break;
        }

        q = PVRTLCQ_PVRTCNormal;
        switch (f)
        {
        case TextureFormat::PVRTC_RGB2:       pf = PVRTLPF_PVRTCI_2bpp_RGB;  return true;
        case TextureFormat::PVRTC_RGBA2:      pf = PVRTLPF_PVRTCI_2bpp_RGBA; return true;
        case TextureFormat::PVRTC_RGB4:       pf = PVRTLPF_PVRTCI_4bpp_RGB;  return true;
        case TextureFormat::PVRTC_RGBA4:      pf = PVRTLPF_PVRTCI_4bpp_RGBA; return true;
        default: break;
        }

        q = PVRTLCQ_ASTCMedium;
        switch (f)
        {
        case TextureFormat::ASTC_RGB_4x4:   case TextureFormat::ASTC_RGBA_4x4:   pf = PVRTLPF_ASTC_4x4;   return true;
        case TextureFormat::ASTC_RGB_5x5:   case TextureFormat::ASTC_RGBA_5x5:   pf = PVRTLPF_ASTC_5x5;   return true;
        case TextureFormat::ASTC_RGB_6x6:   case TextureFormat::ASTC_RGBA_6x6:   pf = PVRTLPF_ASTC_6x6;   return true;
        case TextureFormat::ASTC_RGB_8x8:   case TextureFormat::ASTC_RGBA_8x8:   pf = PVRTLPF_ASTC_8x8;   return true;
        case TextureFormat::ASTC_RGB_10x10: case TextureFormat::ASTC_RGBA_10x10: pf = PVRTLPF_ASTC_10x10; return true;
        case TextureFormat::ASTC_RGB_12x12: case TextureFormat::ASTC_RGBA_12x12: pf = PVRTLPF_ASTC_12x12; return true;
        case TextureFormat::ASTC_HDR_4x4:   pf = PVRTLPF_ASTC_4x4;   vt = PVRTLVT_SignedFloat; hdr = true; return true;
        case TextureFormat::ASTC_HDR_5x5:   pf = PVRTLPF_ASTC_5x5;   vt = PVRTLVT_SignedFloat; hdr = true; return true;
        case TextureFormat::ASTC_HDR_6x6:   pf = PVRTLPF_ASTC_6x6;   vt = PVRTLVT_SignedFloat; hdr = true; return true;
        case TextureFormat::ASTC_HDR_8x8:   pf = PVRTLPF_ASTC_8x8;   vt = PVRTLVT_SignedFloat; hdr = true; return true;
        case TextureFormat::ASTC_HDR_10x10: pf = PVRTLPF_ASTC_10x10; vt = PVRTLVT_SignedFloat; hdr = true; return true;
        case TextureFormat::ASTC_HDR_12x12: pf = PVRTLPF_ASTC_12x12; vt = PVRTLVT_SignedFloat; hdr = true; return true;
        default: return false;
        }
    }

    bool IsPvrFormat(TextureFormat f)
    {
        uint64_t pf; PVRTexLibVariableType vt; PVRTexLibCompressorQuality q; bool hdr;
        return MapPvrEnc(f, pf, vt, q, hdr);
    }

    bool EncodePvr(const Src& s, int w, int h, TextureFormat f, std::vector<uint8_t>& out)
    {
        uint64_t pf; PVRTexLibVariableType vt; PVRTexLibCompressorQuality q; bool isHdr;
        if (!MapPvrEnc(f, pf, vt, q, isHdr)) return false;

        const size_t n = (size_t)w * h;
        std::vector<uint8_t> in;
        uint64_t inPF;
        PVRTexLibVariableType inVT;

        if (isHdr)
        {
            inPF = PVRTGENPIXELID4('r', 'g', 'b', 'a', 32, 32, 32, 32);
            inVT = PVRTLVT_SignedFloat;
            in.resize(n * 16);
            for (size_t i = 0; i < n; ++i)
            {
                float r, g, b, a;
                s.get(i, r, g, b, a);
                WF(in.data() + i * 16, r); WF(in.data() + i * 16 + 4, g);
                WF(in.data() + i * 16 + 8, b); WF(in.data() + i * 16 + 12, a);
            }
        }
        else
        {
            inPF = PVRTGENPIXELID4('r', 'g', 'b', 'a', 8, 8, 8, 8);
            inVT = PVRTLVT_UnsignedByteNorm;
            in.resize(n * 4);
            for (size_t i = 0; i < n; ++i)
            {
                float r, g, b, a;
                s.get(i, r, g, b, a);
                uint8_t* o = in.data() + i * 4;
                o[0] = F2B(r); o[1] = F2B(g); o[2] = F2B(b); o[3] = F2B(a);
            }
        }

        pvrtexlib::PVRTextureHeader header(inPF, (uint32_t)w, (uint32_t)h, 1, 1, 1, 1, PVRTLCS_Linear, inVT);
        pvrtexlib::PVRTexture tex(header, in.data());

        if (!tex.Transcode(pf, vt, PVRTLCS_Linear, q, false))
            return false;

        const size_t got = tex.GetTextureDataSize();
        // Verify encoded byte size against expected dimensions to prevent buffer corruption
        if (got != LevelSize(f, w, h) || !tex.GetTextureDataPointer())
            return false;

        const uint8_t* p = (const uint8_t*)tex.GetTextureDataPointer();
        out.insert(out.end(), p, p + got);
        return true;
    }
#endif

#if ENCODER_USE_CRUNCH
    // ------------------------------------------------------------------------
    //  4. Crunch Compression
    //     Mipmap chain generation is handled internally by crnlib.
    // ------------------------------------------------------------------------
    bool EncodeCrunch(const uint8_t* rgba8, int w, int h, TextureFormat f, int mips, std::vector<uint8_t>& out)
    {
        crn_comp_params cp;
        cp.m_width = w;
        cp.m_height = h;
        cp.set_flag(cCRNCompFlagPerceptual, true);
        cp.set_flag(cCRNCompFlagHierarchical, true);
        cp.m_file_type = cCRNFileTypeCRN;

        switch (f)
        {
        case TextureFormat::DXT1Crunched:       cp.m_format = cCRNFmtDXT1;  break;
        case TextureFormat::DXT5Crunched:       cp.m_format = cCRNFmtDXT5;  break;
        case TextureFormat::ETC_RGB4Crunched:   cp.m_format = cCRNFmtETC1;  break;
        case TextureFormat::ETC2_RGBA8Crunched: cp.m_format = cCRNFmtETC2A; break;
        default: return false;
        }

        // Align pixel buffer to 4-byte boundaries
        std::vector<crn_uint32> pixels((size_t)w * h);
        std::memcpy(pixels.data(), rgba8, (size_t)w * h * 4);

        cp.m_pImages[0][0] = pixels.data();
        cp.m_quality_level = 128;          // Default Crunch quality level (cDefaultCRNQualityLevel)
        cp.m_userdata0 = 1;                // Unity Crunch container version header
        cp.m_num_helper_threads = 1;

        crn_mipmap_params mp;
        mp.m_gamma_filtering = true;
        if (mips > (int)cCRNMaxLevels) mips = (int)cCRNMaxLevels;
        if (mips < 1) mips = 1;
        mp.m_mode = (mips == 1) ? cCRNMipModeNoMips : cCRNMipModeGenerateMips;
        mp.m_max_levels = mips;

        crn_uint32 actualQuality = 0, size = 0;
        float actualBitrate = 0.0f;
        void* blob = crn_compress(cp, mp, size, &actualQuality, &actualBitrate);
        if (!blob || size == 0) return false;

        const uint8_t* b = (const uint8_t*)blob;
        out.assign(b, b + size);
#if ENCODER_CRUNCH_FREE_BLOCK
        std::free(blob);
#endif
        return true;
    }
#endif

    bool EncodeLevel(const uint8_t* px, bool isF, int w, int h, TextureFormat f, int quality, std::vector<uint8_t>& out)
    {
        Src s{ px, isF };
        if (IsRawFormat(f)) return EncodeRaw(s, w, h, f, out);
#if ENCODER_USE_ISPC
        switch (f)
        {
        case TextureFormat::DXT1: case TextureFormat::DXT5: case TextureFormat::BC4:
        case TextureFormat::BC5:  case TextureFormat::BC6H: case TextureFormat::BC7:
            return EncodeBC(s, w, h, f, quality, out);
        default: break;
        }
#endif
#if ENCODER_USE_PVRTEXLIB
        if (IsPvrFormat(f)) return EncodePvr(s, w, h, f, out);
#endif
        (void)quality;
        return false;
    }

    // ------------------------------------------------------------------------
    //  Encoded Data Storage and Thread-Safe Handoff
    // ------------------------------------------------------------------------
    std::mutex g_mutex;
    std::unordered_map<int, std::vector<uint8_t>> g_results;
    int g_nextId = 1;

    inline void Report(ProgressCallback cb, float p, const char* msg) { if (cb) cb(p, msg); }
}

// ============================================================================
//  Exported API
// ============================================================================

// Encoded byte size for the complete mipmap chain (returns 0 for Crunch or unsupported formats)
EXPORT uint32_t __cdecl GetEncodedSizeNative(TextureFormat format, int32_t width, int32_t height, int32_t mipCount)
{
    if (width <= 0 || height <= 0 || IsCrunch(format)) return 0;
    const int maxM = MaxMips(width, height);
    const int mips = mipCount <= 0 ? maxM : (std::min)(mipCount, maxM);
    size_t total = 0;
    for (int i = 0; i < mips; ++i)
    {
        const size_t sz = LevelSize(format, (size_t)(std::max)(1, width >> i), (size_t)(std::max)(1, height >> i));
        if (!sz) return 0;
        total += sz;
    }
    return (uint32_t)total;
}

//  inputPixels : Mip 0; RGBA8 (R, G, B, A) layout, or RGBA32F if flags contain ENC_INPUT_FLOAT
//  quality     : Compression effort [0..5] (BC7: <=1 fast, 2-3 standard, >=4 high/slow; defaults applied for other formats)
//  mipCount    : Target mip levels (1 = mip 0 only, <=0 = full chain down to 1x1, >1 = exact requested count)
//  outMipCount : Optional output receiving the total generated mip levels (maps to m_MipCount)
//  outHandle   : Handle identifier returned for retrieval and cleanup via PickUpAndFree
//  return      : Total size in bytes of the encoded buffer, or 0 on failure
EXPORT uint32_t __cdecl EncodeTextureNative(
    const uint8_t* inputPixels,
    int32_t width,
    int32_t height,
    TextureFormat format,
    int32_t quality,
    int32_t mipCount,
    int32_t flags,
    int32_t* outMipCount,
    int32_t* outHandle,
    ProgressCallback onProgress)
{
    if (outHandle) *outHandle = 0;
    if (outMipCount) *outMipCount = 0;
    if (!inputPixels || !outHandle || width <= 0 || height <= 0)
        return 0;

    try
    {
        const bool inF = (flags & ENC_INPUT_FLOAT) != 0;
        const bool srgb = (flags & ENC_SRGB_MIPS) != 0;
        const int  maxM = MaxMips(width, height);
        const int  mips = mipCount <= 0 ? maxM : (std::min)(mipCount, maxM);
        const size_t bpp = inF ? 16 : 4;

        std::vector<uint8_t> result;

        if (IsCrunch(format))
        {
#if ENCODER_USE_CRUNCH
            Report(onProgress, 0.0f, "Crunch compressing...");
            std::vector<uint8_t> rgba8;
            const uint8_t* src8 = inputPixels;
            if (inF)
            {
                const size_t n = (size_t)width * height;
                rgba8.resize(n * 4);
                Src s{ inputPixels, true };
                for (size_t i = 0; i < n; ++i)
                {
                    float r, g, b, a;
                    s.get(i, r, g, b, a);
                    rgba8[i * 4] = F2B(r); rgba8[i * 4 + 1] = F2B(g); rgba8[i * 4 + 2] = F2B(b); rgba8[i * 4 + 3] = F2B(a);
                }
                src8 = rgba8.data();
            }
            if (!EncodeCrunch(src8, width, height, format, mips, result)) return 0;
#else
            return 0;
#endif
        }
        else
        {
            if (!LevelSize(format, (size_t)width, (size_t)height)) return 0;

            result.reserve(GetEncodedSizeNative(format, width, height, mips));

            const uint8_t* cur = inputPixels;
            std::vector<uint8_t> bufA, bufB;
            int cw = width, ch = height;

            for (int i = 0; i < mips; ++i)
            {
                const size_t before = result.size();
                if (!EncodeLevel(cur, inF, cw, ch, format, quality, result))
                    return 0;
                if (result.size() - before != LevelSize(format, (size_t)cw, (size_t)ch))
                    return 0; // Abort if output payload length does not match expected mip bounds

                Report(onProgress, (float)(i + 1) / (float)mips, "Encoding mip levels...");

                if (i + 1 < mips)
                {
                    const int nw = (std::max)(1, cw >> 1), nh = (std::max)(1, ch >> 1);
                    std::vector<uint8_t>& dst = (cur == bufA.data() && !bufA.empty()) ? bufB : bufA;
                    dst.resize((size_t)nw * nh * bpp);
                    if (inF) DownF(cur, cw, ch, dst.data(), nw, nh);
                    else     Down8(cur, cw, ch, dst.data(), nw, nh, srgb);
                    cur = dst.data();
                    cw = nw; ch = nh;
                }
            }
        }

        if (result.empty() || result.size() > 0xFFFFFFFFull) return 0;

        const uint32_t size = (uint32_t)result.size();
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            const int id = g_nextId++;
            g_results[id] = std::move(result);
            *outHandle = id;
        }
        if (outMipCount) *outMipCount = mips;
        Report(onProgress, 1.0f, "Done");
        return size;
    }
    catch (...)
    {
        return 0;
    }
}

// Copies encoded data to the buffer and frees internal storage.
// Passing outBuf == nullptr only frees the entry (used for cancellation or cleanup).
EXPORT bool __cdecl PickUpAndFree(void* outBuf, uint32_t size, int32_t id)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_results.find(id);
    if (it == g_results.end()) return false;

    bool ok = true;
    if (outBuf)
    {
        if (size > it->second.size()) ok = false;
        else std::memcpy(outBuf, it->second.data(), size);
    }
    g_results.erase(it);
    return ok;
}