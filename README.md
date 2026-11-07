<div align="center">

# YHG Assets Handler

**High-performance desktop utility for inspecting, extracting, and modifying Unity AssetBundles and serialized assets.**

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)
[![Architecture](https://img.shields.io/badge/Architecture-x64-555555?style=for-the-badge)](#system-requirements)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE)

<br>

```
Inspect  •  Extract  •  Edit  •  Replace
```

A dedicated native workspace engineered for Unity `.assets` files and AssetBundle archives.

<br>

[![Standalone Download](https://img.shields.io/badge/Download-Standalone_(x64)-2563EB?style=for-the-badge&logo=windows)](https://github.com/yhgindian/yhg-assets-handler/releases/latest/download/YHG-Assets-Handler-win-x64-Standalone.zip)
&nbsp;
[![Lightweight Download](https://img.shields.io/badge/Download-Lightweight_(x64)-333333?style=for-the-badge&logo=windows)](https://github.com/yhgindian/yhg-assets-handler/releases/latest/download/YHG-Assets-Handler-win-x64-Lightweight.zip)

<br>

[**Releases Archive**](https://github.com/yhgindian/yhg-assets-handler/releases)
&nbsp; • &nbsp;
[**Issue Tracker**](https://github.com/yhgindian/yhg-assets-handler/issues)
&nbsp; • &nbsp;
[**Tools Portal**](https://tools.yhgindian.com)

</div>

---

## Overview

**YHG Assets Handler** is a modular Windows desktop utility designed for inspecting, extracting, editing, and repacking assets within Unity AssetBundles and raw serialized container files (`.assets`).

Built with modern **.NET 10** and native hardware-accelerated interfaces, it bridges low-level asset parsing with a responsive workspace tailored for game developers, technical artists, and modding engineers.

---

## Core Capabilities

<table>
<tr>
<td width="50%" valign="top">

### 📦 AssetBundle Support
Inspect compressed and uncompressed Unity AssetBundles with instant directory breakdown and recursive hierarchy navigation.

</td>
<td width="50%" valign="top">

### 🗂️ Serialized Asset Support
Directly inspect, read, and deserialize raw `.assets` and resource container structures.

</td>
</tr>
<tr>
<td width="50%" valign="top">

### 🔍 Advanced Asset Explorer
Instant indexing, multi-rule filtering, and search engine for deep asset graph discovery.

</td>
<td width="50%" valign="top">

### 📤 High-Speed Asset Extraction
Batch or selective extraction of binary objects, text assets, scripts, and media streams.

</td>
</tr>
<tr>
<td width="50%" valign="top">

### ✏️ In-Place Asset Replacement
Direct binary substitution for supported object types with automatic pointer re-indexing.

</td>
<td width="50%" valign="top">

### 🖼️ Texture2D Pipeline
Lossless PNG exporting and importing pipeline for direct runtime texture modifications and replacements.

</td>
</tr>
<tr>
<td width="50%" valign="top">

### 🧊 3D Mesh Processing
Parse, extract, and swap 3D submeshes, vertex buffers, and normal attributes.

</td>
<td width="50%" valign="top">

### 💡 VS Code Integration
Auto-completion schema and direct editing support for text-based asset types via Visual Studio Code.

</td>
</tr>
<tr>
<td colspan="2" valign="top">

### ⚙️ Deep Metadata Inspection
Read typetrees, class IDs, memory offsets, and container headers directly without external dependencies.

</td>
</tr>
</table>

---

## Downloads & Distribution

Choose the edition best suited for your environment.

| Package | Standalone Edition | Lightweight Edition |
|---|---|---|
| **Distribution** | Fully self-contained portable package | Compact framework-dependent binary |
| **Prerequisites** | None (All runtimes bundled) | Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Architecture** | Windows x64 | Windows x64 |
| **Best Suited For** | Zero-setup immediate deployment | Fast updates and minimal disk usage |

<div align="center">

### Latest Stable Release

[![Standalone Download](https://img.shields.io/badge/Download-Standalone_(x64)-2563EB?style=for-the-badge&logo=windows)](https://github.com/yhgindian/yhg-assets-handler/releases/latest/download/YHG-Assets-Handler-win-x64-Standalone.zip)
&nbsp;
[![Lightweight Download](https://img.shields.io/badge/Download-Lightweight_(x64)-333333?style=for-the-badge&logo=windows)](https://github.com/yhgindian/yhg-assets-handler/releases/latest/download/YHG-Assets-Handler-win-x64-Lightweight.zip)

[**View All Release Tags & Changelogs →**](https://github.com/yhgindian/yhg-assets-handler/releases)

</div>

---

## System Requirements

| Parameter | Minimum Requirement | Recommended Specification |
|---|---|---|
| **OS** | Windows 10 x64 (Build 1909+) | Windows 11 x64 |
| **Processor** | Intel Core i3 / AMD Ryzen 3 (64-bit) | Quad-Core Intel Core i5 / AMD Ryzen 5 or better |
| **Memory** | 4 GB RAM | 8 GB+ RAM (For large bundle indexing) |
| **Storage** | 200 MB free space | NVMe SSD for fast large-file I/O |
| **Display** | 1280 x 720 | 1920 x 1080 or higher (DPI-aware) |
| **Runtime** | Bundled (Standalone) | .NET 10 Desktop Runtime x64 (Lightweight) |

---

## Third-Party Libraries & Credits

YHG Assets Handler integrates verified open-source libraries and subcomponents:

| Library / Engine | Purpose | License |
|---|---|---|
| **YHG.FastBF** | Zero-dependency high-speed MS-NRBF binary formatter engine | MIT |
| **AssetsTools.NET** | Unity serialized file & asset bundle decoding engine | MIT |
| **AssimpNet** | 3D model parsing and scene abstraction wrapper | MIT / BSD-3-Clause |
| **CommunityToolkit.Mvvm** | Modern, decoupled MVVM architecture primitives | MIT |
| **Newtonsoft.Json** | High-performance JSON schema & serialization | MIT |
| **MahApps.Metro.IconPacks** | Vector icon packs and UI resource assets | MIT |
| **Fmod5Sharp** | FMOD soundbank format reader and decoder | MIT |
| **StbImageSharp** | Managed image loading, texture conversion, and decoding | Public Domain / MIT |
| **TagLibSharp** | Media file metadata tagging engine | LGPL-2.1 |
| **bcdec** | BC1–BC7 texture decompression routines | MIT / Unlicense |
| **Crunch** | Advanced DXT compression and mipmap crunching engine | Public Domain |
| **PVRTexLib** | PowerVR texture compression and container utilities | PowerVR Tools EULA |

*Third-party components remain subject to their respective licenses and terms.*

---

## Support & Contributions

If you find YHG Assets Handler useful for your workflow, consider supporting development:

<div align="center">

### Support via UPI
`yellowhostgamingbs1@oksbi`

<br>

[![Buy Me a Coffee](https://img.shields.io/badge/Buy_Me_a_Coffee-Support_Development-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=000000)](https://buymeacoffee.com/yhgindian)

</div>

---

## License

**YHG Assets Handler** is proprietary software.

The application and its core proprietary components are protected by international copyright laws. Unauthorized redistribution, modification, decompilation, or reverse engineering is strictly prohibited except where explicitly permitted by law.

Third-party dependencies and submodules bundled within the distribution remain governed by their respective open-source licenses.

---

<div align="center">

**YHG Assets Handler** • Developed by [yhgindian](https://github.com/yhgindian)

[Official Releases](https://github.com/yhgindian/yhg-assets-handler/releases) • [YHG Tools](https://tools.yhgindian.com)

</div>