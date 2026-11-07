// ============================================================================
//  TextureDecoder.cpp  -  Unity texture -> RGBA32 (R, G, B, A byte order)
//
//  Dependencies:
//    * bcdec.h        (single header, public domain/MIT)  -> DXT1/3/5, BC4/5/6H/7
//    * PVRTexLib      -> ETC1/ETC2/EAC/PVRTC/ASTC
//    * crn_decomp.h   -> Crunch decompression (optional)
//
//  Feature toggles can be overridden via preprocessor flags:
//    -DDECODER_USE_PVRTEXLIB=0  -DDECODER_USE_CRUNCH=0
//
//  NOTE: crn_decomp.h contains inline implementation. If crn_decomp.h is
//  included across multiple translation units in the same project, define
//  CRND_HEADER_FILE_ONLY in all but one translation unit to prevent duplicate
//  symbol linker errors (LNK2005 / multiple definition).
// ============================================================================
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include "pch.h"
#include "Export.h"
#include "TextureFormat.h"

#include <cstdint>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <vector>

#ifndef DECODER_USE_PVRTEXLIB
#define DECODER_USE_PVRTEXLIB 1
#endif
#ifndef DECODER_USE_CRUNCH
#define DECODER_USE_CRUNCH 1
#endif

#define BCDEC_IMPLEMENTATION
#include "bcdec.h"

#if DECODER_USE_PVRTEXLIB
#include "PVRTexLib/Include/PVRTexLib.hpp"
#endif
#if DECODER_USE_CRUNCH
#include "crunch/inc/crn_decomp.h"
#endif

bool AstcBlockDims(TextureFormat f, int& bx, int& by, bool& hdr);

namespace
{
    // ------------------------------------------------------------------------
    //  Small helpers
    // ------------------------------------------------------------------------
    inline uint16_t Rd16(const uint8_t* p) { uint16_t v; std::memcpy(&v, p, 2); return v; }
    inline uint32_t Rd32(const uint8_t* p) { uint32_t v; std::memcpy(&v, p, 4); return v; }
    inline float    RdF(const uint8_t* p) { float v;    std::memcpy(&v, p, 4); return v; }

    inline void Put(uint8_t* d, uint8_t r, uint8_t g, uint8_t b, uint8_t a)
    {
        d[0] = r; d[1] = g; d[2] = b; d[3] = a;
    }

    inline uint8_t F2B(float v)
    {
        if (!(v > 0.0f)) return 0;            // negative + NaN
        if (v >= 1.0f)   return 255;          // clamp (HDR / inf)
        return (uint8_t)(v * 255.0f + 0.5f);
    }

    inline uint8_t E4(uint32_t v) { return (uint8_t)(v * 17); }
    inline uint8_t E5(uint32_t v) { return (uint8_t)((v << 3) | (v >> 2)); }
    inline uint8_t E6(uint32_t v) { return (uint8_t)((v << 2) | (v >> 4)); }

    float HalfToFloat(uint16_t h)
    {
        const uint32_t s = (h >> 15) & 1u;
        const uint32_t e = (h >> 10) & 0x1Fu;
        const uint32_t m = h & 0x3FFu;
        float f;
        if (e == 0)       f = std::ldexp((float)m, -24);
        else if (e == 31) f = m ? NAN : INFINITY;
        else              f = std::ldexp((float)(m | 0x400u), (int)e - 25);
        return s ? -f : f;
    }

    inline size_t Ceil(size_t a, size_t b) { return (a + b - 1) / b; }

    // ------------------------------------------------------------------------
    //  Expected size of mip 0 (buffer overread se bachne ke liye)
    //  return 0  => unknown / unsupported
    // ------------------------------------------------------------------------
    size_t ExpectedSize(TextureFormat f, size_t w, size_t h)
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
        case TextureFormat::RGBAFloat:
        case TextureFormat::ARGBFloat:     return px * 16;

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

            // PVRTC1: minimum 2x2 blocks
        case TextureFormat::PVRTC_RGB2:
        case TextureFormat::PVRTC_RGBA2:
            return (std::max<size_t>)(Ceil(w, 8), 2) * (std::max<size_t>)(Ceil(h, 4), 2) * 8;
        case TextureFormat::PVRTC_RGB4:
        case TextureFormat::PVRTC_RGBA4:
            return (std::max<size_t>)(Ceil(w, 4), 2) * (std::max<size_t>)(Ceil(h, 4), 2) * 8;

        default: break;
        }

        int bx = 0, by = 0;
        bool hdr;
        if (AstcBlockDims(f, bx, by, hdr))
            return Ceil(w, bx) * Ceil(h, by) * 16;

        return 0;
    }
}

// ----------------------------------------------------------------------------
//  ASTC helpers (namespace ke bahar kyunki ExpectedSize use karta hai)
// ----------------------------------------------------------------------------
bool AstcBlockDims(TextureFormat f, int& bx, int& by, bool& hdr)
{
    hdr = false;
    switch (f)
    {
    case TextureFormat::ASTC_RGB_4x4:  case TextureFormat::ASTC_RGBA_4x4:  bx = by = 4;  return true;
    case TextureFormat::ASTC_RGB_5x5:  case TextureFormat::ASTC_RGBA_5x5:  bx = by = 5;  return true;
    case TextureFormat::ASTC_RGB_6x6:  case TextureFormat::ASTC_RGBA_6x6:  bx = by = 6;  return true;
    case TextureFormat::ASTC_RGB_8x8:  case TextureFormat::ASTC_RGBA_8x8:  bx = by = 8;  return true;
    case TextureFormat::ASTC_RGB_10x10:case TextureFormat::ASTC_RGBA_10x10:bx = by = 10; return true;
    case TextureFormat::ASTC_RGB_12x12:case TextureFormat::ASTC_RGBA_12x12:bx = by = 12; return true;
    case TextureFormat::ASTC_HDR_4x4:   hdr = true; bx = by = 4;  return true;
    case TextureFormat::ASTC_HDR_5x5:   hdr = true; bx = by = 5;  return true;
    case TextureFormat::ASTC_HDR_6x6:   hdr = true; bx = by = 6;  return true;
    case TextureFormat::ASTC_HDR_8x8:   hdr = true; bx = by = 8;  return true;
    case TextureFormat::ASTC_HDR_10x10: hdr = true; bx = by = 10; return true;
    case TextureFormat::ASTC_HDR_12x12: hdr = true; bx = by = 12; return true;
    default: return false;
    }
}

namespace
{
    // ------------------------------------------------------------------------
    //  1. Uncompressed / raw formats  -> RGBA8
    // ------------------------------------------------------------------------
    bool DecodeRaw(const uint8_t* s, int w, int h, TextureFormat f, uint8_t* d)
    {
        const size_t n = (size_t)w * (size_t)h;

        switch (f)
        {
        case TextureFormat::RGBA32:
            std::memcpy(d, s, n * 4);
            return true;

        case TextureFormat::BGRA32:
        case TextureFormat::BGRA32Old:
            for (size_t i = 0; i < n; ++i, s += 4, d += 4) Put(d, s[2], s[1], s[0], s[3]);
            return true;

        case TextureFormat::ARGB32:   // memory: A R G B
            for (size_t i = 0; i < n; ++i, s += 4, d += 4) Put(d, s[1], s[2], s[3], s[0]);
            return true;

        case TextureFormat::RGB24:
            for (size_t i = 0; i < n; ++i, s += 3, d += 4) Put(d, s[0], s[1], s[2], 255);
            return true;

        case TextureFormat::BGR24:
            for (size_t i = 0; i < n; ++i, s += 3, d += 4) Put(d, s[2], s[1], s[0], 255);
            return true;

        case TextureFormat::Alpha8:
            for (size_t i = 0; i < n; ++i, s += 1, d += 4) Put(d, 255, 255, 255, s[0]);
            return true;

        case TextureFormat::R8:
            for (size_t i = 0; i < n; ++i, s += 1, d += 4) Put(d, s[0], 0, 0, 255);
            return true;

        case TextureFormat::RG16:     // 2 x uint8
            for (size_t i = 0; i < n; ++i, s += 2, d += 4) Put(d, s[0], s[1], 0, 255);
            return true;

        case TextureFormat::R16:      // 1 x uint16
            for (size_t i = 0; i < n; ++i, s += 2, d += 4) Put(d, (uint8_t)(Rd16(s) >> 8), 0, 0, 255);
            return true;

        case TextureFormat::RG32:     // 2 x uint16
            for (size_t i = 0; i < n; ++i, s += 4, d += 4)
                Put(d, (uint8_t)(Rd16(s) >> 8), (uint8_t)(Rd16(s + 2) >> 8), 0, 255);
            return true;

        case TextureFormat::RGB48:    // 3 x uint16
            for (size_t i = 0; i < n; ++i, s += 6, d += 4)
                Put(d, (uint8_t)(Rd16(s) >> 8), (uint8_t)(Rd16(s + 2) >> 8), (uint8_t)(Rd16(s + 4) >> 8), 255);
            return true;

        case TextureFormat::RGBA64:   // 4 x uint16
            for (size_t i = 0; i < n; ++i, s += 8, d += 4)
                Put(d, (uint8_t)(Rd16(s) >> 8), (uint8_t)(Rd16(s + 2) >> 8),
                    (uint8_t)(Rd16(s + 4) >> 8), (uint8_t)(Rd16(s + 6) >> 8));
            return true;

        case TextureFormat::RGBA4444: // R(15-12) G(11-8) B(7-4) A(3-0)
            for (size_t i = 0; i < n; ++i, s += 2, d += 4)
            {
                uint32_t v = Rd16(s);
                Put(d, E4((v >> 12) & 15), E4((v >> 8) & 15), E4((v >> 4) & 15), E4(v & 15));
            }
            return true;

        case TextureFormat::ARGB4444: // A(15-12) R(11-8) G(7-4) B(3-0)
            for (size_t i = 0; i < n; ++i, s += 2, d += 4)
            {
                uint32_t v = Rd16(s);
                Put(d, E4((v >> 8) & 15), E4((v >> 4) & 15), E4(v & 15), E4((v >> 12) & 15));
            }
            return true;

        case TextureFormat::RGB565:   // R(15-11) G(10-5) B(4-0)
            for (size_t i = 0; i < n; ++i, s += 2, d += 4)
            {
                uint32_t v = Rd16(s);
                Put(d, E5((v >> 11) & 31), E6((v >> 5) & 63), E5(v & 31), 255);
            }
            return true;

        case TextureFormat::RHalf:
            for (size_t i = 0; i < n; ++i, s += 2, d += 4)
                Put(d, F2B(HalfToFloat(Rd16(s))), 0, 0, 255);
            return true;

        case TextureFormat::RGHalf:
            for (size_t i = 0; i < n; ++i, s += 4, d += 4)
                Put(d, F2B(HalfToFloat(Rd16(s))), F2B(HalfToFloat(Rd16(s + 2))), 0, 255);
            return true;

        case TextureFormat::RGBAHalf:
            for (size_t i = 0; i < n; ++i, s += 8, d += 4)
                Put(d, F2B(HalfToFloat(Rd16(s))), F2B(HalfToFloat(Rd16(s + 2))),
                    F2B(HalfToFloat(Rd16(s + 4))), F2B(HalfToFloat(Rd16(s + 6))));
            return true;

        case TextureFormat::RFloat:
            for (size_t i = 0; i < n; ++i, s += 4, d += 4)
                Put(d, F2B(RdF(s)), 0, 0, 255);
            return true;

        case TextureFormat::RGFloat:
            for (size_t i = 0; i < n; ++i, s += 8, d += 4)
                Put(d, F2B(RdF(s)), F2B(RdF(s + 4)), 0, 255);
            return true;

        case TextureFormat::RGBFloat:
            for (size_t i = 0; i < n; ++i, s += 12, d += 4)
                Put(d, F2B(RdF(s)), F2B(RdF(s + 4)), F2B(RdF(s + 8)), 255);
            return true;

        case TextureFormat::RGBAFloat:
            for (size_t i = 0; i < n; ++i, s += 16, d += 4)
                Put(d, F2B(RdF(s)), F2B(RdF(s + 4)), F2B(RdF(s + 8)), F2B(RdF(s + 12)));
            return true;

        case TextureFormat::RGB9e5Float:  // r:0-8 g:9-17 b:18-26 e:27-31
            for (size_t i = 0; i < n; ++i, s += 4, d += 4)
            {
                uint32_t v = Rd32(s);
                float scale = std::ldexp(1.0f, (int)(v >> 27) - 15 - 9);
                Put(d, F2B((float)(v & 0x1FF) * scale),
                    F2B((float)((v >> 9) & 0x1FF) * scale),
                    F2B((float)((v >> 18) & 0x1FF) * scale), 255);
            }
            return true;

        case TextureFormat::YUY2:         // Y0 U Y1 V  (BT.601)
        {
            auto conv = [](int Y, int U, int V, uint8_t* o)
                {
                    int c = Y - 16, dd = U - 128, e = V - 128;
                    int r = (298 * c + 409 * e + 128) >> 8;
                    int g = (298 * c - 100 * dd - 208 * e + 128) >> 8;
                    int b = (298 * c + 516 * dd + 128) >> 8;
                    Put(o, (uint8_t)std::clamp(r, 0, 255), (uint8_t)std::clamp(g, 0, 255),
                        (uint8_t)std::clamp(b, 0, 255), 255);
                };
            for (int y = 0; y < h; ++y)
            {
                const uint8_t* row = s + (size_t)y * w * 2;
                uint8_t* out = d + (size_t)y * w * 4;
                for (int x = 0; x < w; x += 2)
                {
                    const uint8_t* q = row + (size_t)x * 2;   // Y0 U Y1 V
                    conv(q[0], q[1], q[3], out + (size_t)x * 4);
                    if (x + 1 < w) conv(q[2], q[1], q[3], out + (size_t)(x + 1) * 4);
                }
            }
            return true;
        }

        default:
            return false;
        }
    }

    // ------------------------------------------------------------------------
    //  2. BC / DXT  (bcdec)
    //  decodeBlock(src, rgba4x4[64 bytes])
    // ------------------------------------------------------------------------
    template <class F>
    void DecodeBlocks(const uint8_t* src, int w, int h, int blockBytes, uint8_t* dst, F decodeBlock)
    {
        const int bx = (w + 3) / 4, by = (h + 3) / 4;
        uint8_t tmp[64];
        for (int y = 0; y < by; ++y)
        {
            for (int x = 0; x < bx; ++x)
            {
                decodeBlock(src, tmp);
                src += blockBytes;

                const int cw = (std::min)(4, w - x * 4);
                const int ch = (std::min)(4, h - y * 4);
                for (int j = 0; j < ch; ++j)
                {
                    std::memcpy(dst + ((size_t)(y * 4 + j) * w + (size_t)x * 4) * 4,
                        tmp + j * 16, (size_t)cw * 4);
                }
            }
        }
    }

    bool DecodeBC(const uint8_t* s, int w, int h, TextureFormat f, uint8_t* d)
    {
        switch (f)
        {
        case TextureFormat::DXT1:
            DecodeBlocks(s, w, h, 8, d, [](const uint8_t* b, uint8_t* o) { bcdec_bc1(b, o, 16); });
            return true;
        case TextureFormat::DXT3:
            DecodeBlocks(s, w, h, 16, d, [](const uint8_t* b, uint8_t* o) { bcdec_bc2(b, o, 16); });
            return true;
        case TextureFormat::DXT5:
            DecodeBlocks(s, w, h, 16, d, [](const uint8_t* b, uint8_t* o) { bcdec_bc3(b, o, 16); });
            return true;
        case TextureFormat::BC7:
            DecodeBlocks(s, w, h, 16, d, [](const uint8_t* b, uint8_t* o) { bcdec_bc7(b, o, 16); });
            return true;

        case TextureFormat::BC4:
            DecodeBlocks(s, w, h, 8, d, [](const uint8_t* b, uint8_t* o)
                {
                    uint8_t r[16];
                    bcdec_bc4(b, r, 4);                 // 1 byte/pixel, pitch 4
                    for (int i = 0; i < 16; ++i) Put(o + i * 4, r[i], 0, 0, 255);
                });
            return true;

        case TextureFormat::BC5:
            DecodeBlocks(s, w, h, 16, d, [](const uint8_t* b, uint8_t* o)
                {
                    uint8_t rg[32];
                    bcdec_bc5(b, rg, 8);                // 2 bytes/pixel, pitch 8
                    for (int i = 0; i < 16; ++i) Put(o + i * 4, rg[i * 2], rg[i * 2 + 1], 0, 255);
                });
            return true;

        case TextureFormat::BC6H:
            DecodeBlocks(s, w, h, 16, d, [](const uint8_t* b, uint8_t* o)
                {
                    float rgb[48];
                    bcdec_bc6h_float(b, rgb, 12, 0);    // pitch floats me, unsigned
                    for (int i = 0; i < 16; ++i)
                        Put(o + i * 4, F2B(rgb[i * 3]), F2B(rgb[i * 3 + 1]), F2B(rgb[i * 3 + 2]), 255);
                });
            return true;

        default:
            return false;
        }
    }

#if DECODER_USE_PVRTEXLIB
    // ------------------------------------------------------------------------
    //  3. ETC / EAC / PVRTC  (PVRTexLib)
    // ------------------------------------------------------------------------
    bool MapPvr(TextureFormat f, uint64_t& pf, PVRTexLibVariableType& vt)
    {
        vt = PVRTLVT_UnsignedByteNorm;
        switch (f)
        {
        case TextureFormat::ETC_RGB4:
        case TextureFormat::ETC_RGB4_3DS:     pf = PVRTLPF_ETC1;            return true;
        case TextureFormat::ETC2_RGB4:        pf = PVRTLPF_ETC2_RGB;        return true;
        case TextureFormat::ETC2_RGBA1:       pf = PVRTLPF_ETC2_RGB_A1;     return true;
        case TextureFormat::ETC2_RGBA8:
        case TextureFormat::ETC_RGBA8_3DS:    pf = PVRTLPF_ETC2_RGBA;       return true;
        case TextureFormat::EAC_R:            pf = PVRTLPF_EAC_R11;         return true;
        case TextureFormat::EAC_R_SIGNED:     pf = PVRTLPF_EAC_R11;  vt = PVRTLVT_SignedIntegerNorm; return true;
        case TextureFormat::EAC_RG:           pf = PVRTLPF_EAC_RG11;        return true;
        case TextureFormat::EAC_RG_SIGNED:    pf = PVRTLPF_EAC_RG11; vt = PVRTLVT_SignedIntegerNorm; return true;
        case TextureFormat::PVRTC_RGB2:       pf = PVRTLPF_PVRTCI_2bpp_RGB;  return true;
        case TextureFormat::PVRTC_RGBA2:      pf = PVRTLPF_PVRTCI_2bpp_RGBA; return true;
        case TextureFormat::PVRTC_RGB4:       pf = PVRTLPF_PVRTCI_4bpp_RGB;  return true;
        case TextureFormat::PVRTC_RGBA4:      pf = PVRTLPF_PVRTCI_4bpp_RGBA; return true;

            // ASTC LDR
        case TextureFormat::ASTC_RGB_4x4:
        case TextureFormat::ASTC_RGBA_4x4:    pf = PVRTLPF_ASTC_4x4;   return true;
        case TextureFormat::ASTC_RGB_5x5:
        case TextureFormat::ASTC_RGBA_5x5:    pf = PVRTLPF_ASTC_5x5;   return true;
        case TextureFormat::ASTC_RGB_6x6:
        case TextureFormat::ASTC_RGBA_6x6:    pf = PVRTLPF_ASTC_6x6;   return true;
        case TextureFormat::ASTC_RGB_8x8:
        case TextureFormat::ASTC_RGBA_8x8:    pf = PVRTLPF_ASTC_8x8;   return true;
        case TextureFormat::ASTC_RGB_10x10:
        case TextureFormat::ASTC_RGBA_10x10:  pf = PVRTLPF_ASTC_10x10; return true;
        case TextureFormat::ASTC_RGB_12x12:
        case TextureFormat::ASTC_RGBA_12x12:  pf = PVRTLPF_ASTC_12x12; return true;

            // ASTC HDR (float output path; handled separately in DecodePvr)
        case TextureFormat::ASTC_HDR_4x4:     pf = PVRTLPF_ASTC_4x4;   vt = PVRTLVT_SignedFloat; return true;
        case TextureFormat::ASTC_HDR_5x5:     pf = PVRTLPF_ASTC_5x5;   vt = PVRTLVT_SignedFloat; return true;
        case TextureFormat::ASTC_HDR_6x6:     pf = PVRTLPF_ASTC_6x6;   vt = PVRTLVT_SignedFloat; return true;
        case TextureFormat::ASTC_HDR_8x8:     pf = PVRTLPF_ASTC_8x8;   vt = PVRTLVT_SignedFloat; return true;
        case TextureFormat::ASTC_HDR_10x10:   pf = PVRTLPF_ASTC_10x10; vt = PVRTLVT_SignedFloat; return true;
        case TextureFormat::ASTC_HDR_12x12:   pf = PVRTLPF_ASTC_12x12; vt = PVRTLVT_SignedFloat; return true;
        default: return false;
        }
    }

    bool DecodePvr(const uint8_t* s, int w, int h, TextureFormat f, uint8_t* d)
    {
        uint64_t pf = 0;
        PVRTexLibVariableType vt;
        if (!MapPvr(f, pf, vt)) return false;

        // PVRTexLib expects non-const pointer; cast safely as it is only read
        pvrtexlib::PVRTextureHeader header(pf, (uint32_t)w, (uint32_t)h, 1, 1, 1, 1, PVRTLCS_Linear, vt);
        pvrtexlib::PVRTexture tex(header, const_cast<uint8_t*>(s));

        if (vt == PVRTLVT_SignedFloat)
        {
            // ASTC HDR: Transcode to float RGBA32F first, then clamp to 8-bit
            const uint64_t RGBA32F = PVRTGENPIXELID4('r', 'g', 'b', 'a', 32, 32, 32, 32);
            if (!tex.Transcode(RGBA32F, PVRTLVT_SignedFloat, PVRTLCS_Linear, PVRTLCQ_PVRTCNormal, false))
                return false;

            const size_t n = (size_t)w * h;
            if (tex.GetTextureDataSize() < n * 16) return false;

            const uint8_t* f32 = (const uint8_t*)tex.GetTextureDataPointer();
            for (size_t i = 0; i < n; ++i)
                Put(d + i * 4, F2B(RdF(f32 + i * 16)), F2B(RdF(f32 + i * 16 + 4)),
                    F2B(RdF(f32 + i * 16 + 8)), F2B(RdF(f32 + i * 16 + 12)));
            return true;
        }

        const uint64_t RGBA8888 = PVRTGENPIXELID4('r', 'g', 'b', 'a', 8, 8, 8, 8);
        if (!tex.Transcode(RGBA8888, PVRTLVT_UnsignedByteNorm, PVRTLCS_Linear, PVRTLCQ_PVRTCNormal, false))
            return false;

        const size_t need = (size_t)w * h * 4;
        if (tex.GetTextureDataSize() < need) return false;

        std::memcpy(d, tex.GetTextureDataPointer(), need);
        return true;
    }
#endif


    bool DecodeBlockFormat(const uint8_t* s, size_t len, int w, int h, TextureFormat f, uint8_t* d);

#if DECODER_USE_CRUNCH
    // ------------------------------------------------------------------------
    //  5. Crunch  ->  DXT/ETC block data  ->  normal decode
    // ------------------------------------------------------------------------
    bool DecodeCrunch(const uint8_t* s, size_t len, int w, int h, TextureFormat f, uint8_t* d)
    {
        TextureFormat inner;
        switch (f)
        {
        case TextureFormat::DXT1Crunched:       inner = TextureFormat::DXT1;       break;
        case TextureFormat::DXT5Crunched:       inner = TextureFormat::DXT5;       break;
        case TextureFormat::ETC_RGB4Crunched:   inner = TextureFormat::ETC_RGB4;   break;
        case TextureFormat::ETC2_RGBA8Crunched: inner = TextureFormat::ETC2_RGBA8; break;
        default: return false;
        }

        crnd::crn_texture_info info;
        info.m_struct_size = sizeof(crnd::crn_texture_info);
        if (!crnd::crnd_get_texture_info(s, (crn_uint32)len, &info))
            return false;

        crnd::crnd_unpack_context ctx = crnd::crnd_unpack_begin(s, (crn_uint32)len);
        if (!ctx) return false;

        const crn_uint32 bw = ((std::max<crn_uint32>)(1, info.m_width) + 3) >> 2;
        const crn_uint32 bh = ((std::max<crn_uint32>)(1, info.m_height) + 3) >> 2;
        const crn_uint32 rowPitch = bw * info.m_bytes_per_block;
        const crn_uint32 faceSize = bh * rowPitch;

        std::vector<uint8_t> blocks(faceSize);
        void* dstPtr = blocks.data();
        bool ok = crnd::crnd_unpack_level(ctx, &dstPtr, faceSize, rowPitch, 0);
        crnd::crnd_unpack_end(ctx);
        if (!ok) return false;

        // Actual texture dimensions are parsed from the Crunch header; caller w/h must match.
        if ((int)info.m_width != w || (int)info.m_height != h) return false;

        return DecodeBlockFormat(blocks.data(), blocks.size(), w, h, inner, d);
    }
#endif

    // Common dispatcher for BC, ETC/PVRTC, and ASTC formats (used as an internal pipeline step by Crunch decompression).
    bool DecodeBlockFormat(const uint8_t* s, size_t len, int w, int h, TextureFormat f, uint8_t* d)
    {
        if (DecodeBC(s, w, h, f, d)) return true;

#if DECODER_USE_PVRTEXLIB
        {
            uint64_t pf; PVRTexLibVariableType vt;
            if (MapPvr(f, pf, vt)) return DecodePvr(s, w, h, f, d);
        }
#endif
        (void)len;
        return false;
    }

    inline void Report(ProgressCallback cb, float p, const char* msg)
    {
        if (cb) cb(p, msg);
    }
}

// ============================================================================
//  Exported API
//    outputRgba  : Caller-allocated buffer, width * height * 4 bytes (RGBA layout)
//    inputData   : Encoded source buffer for mipmap level 0
// ============================================================================
EXPORT bool __cdecl DecodeTextureNative(
    const uint8_t* inputData,
    size_t dataLength,
    int32_t width,
    int32_t height,
    TextureFormat format,
    uint8_t* outputRgba,
    ProgressCallback onProgress)
{
    if (!inputData || !outputRgba || width <= 0 || height <= 0)
        return false;

    try
    {
        Report(onProgress, 0.0f, "Starting native decoding...");

        const bool isCrunch =
            format == TextureFormat::DXT1Crunched || format == TextureFormat::DXT5Crunched ||
            format == TextureFormat::ETC_RGB4Crunched || format == TextureFormat::ETC2_RGBA8Crunched;

        // Validate buffer size for uncompressed and fixed-rate block formats; Crunch compression handles dynamic sizes
        if (!isCrunch)
        {
            const size_t need = ExpectedSize(format, (size_t)width, (size_t)height);
            if (need == 0 || dataLength < need)
                return false;
        }

        bool ok = false;

        // 1. Raw / uncompressed formats
        ok = DecodeRaw(inputData, width, height, format, outputRgba);

        // 2. Crunch-compressed formats
        if (!ok && isCrunch)
        {
            Report(onProgress, 0.1f, "Unpacking Crunch...");
#if DECODER_USE_CRUNCH
            ok = DecodeCrunch(inputData, dataLength, width, height, format, outputRgba);
#endif
        }
        // 3. Block-compressed formats (BC, ETC, PVRTC, ASTC)
        else if (!ok)
        {
            Report(onProgress, 0.1f, "Decoding blocks...");
            ok = DecodeBlockFormat(inputData, dataLength, width, height, format, outputRgba);
        }

        if (ok) Report(onProgress, 1.0f, "Done");
        return ok;
    }
    catch (...)
    {
        // Prevent C++ exceptions from crossing the native DLL boundary
        return false;
    }
}