# 🛡️ ZRecover — Sovereign Data Recovery & Forensic Carving Suite

<p align="center">
  <a href="https://github.com/kzxl/ZRecover"><img src="https://img.shields.io/badge/Type-Desktop%20App%20%26%20CLI-007ACC?style=flat-square&logo=windows" alt="Type: Desktop App & CLI" /></a>
  <a href="https://github.com/kzxl/ZRecover"><img src="https://img.shields.io/badge/Ecosystem-Zero%20Universe-8A2BE2?style=flat-square" alt="Ecosystem: Zero Universe" /></a>
  <a href="https://github.com/kzxl/ZRecover"><img src="https://img.shields.io/badge/Platform-Windows%20x64%20(.NET%208)-brightgreen?style=flat-square" alt="Platform: Windows x64 (.NET 8)" /></a>
  <a href="https://github.com/kzxl/ZRecover"><img src="https://img.shields.io/badge/Safety-Zero--Write%20Barrier-crimson?style=flat-square" alt="Safety: Zero-Write Barrier" /></a>
  <a href="https://github.com/kzxl/ZRecover"><img src="https://img.shields.io/badge/License-MIT-blue?style=flat-square" alt="License: MIT" /></a>
</p>

<p align="center">
  <strong>High-performance forensic file recovery and deep disk carving suite for Windows</strong><br/>
  NTFS MFT Parsing • Raw Sector Carving • VSS Snapshot Explorer • Recycle Bin Metadata Extraction • Visual Preview
</p>

---

## 📖 Overview

**ZRecover** is a sovereign forensic data recovery and deep sector carving solution built on pure C# and Windows BCL/Win32 APIs. It enables engineers and incident responders to locate, inspect, and safely recover lost, deleted, or corrupted files across physical disks, logical volumes, and user directories.

Unlike typical commercial utilities that bundle proprietary background telemetry or heavy runtime runtimes, ZRecover adheres to a strict zero-dependency, sovereign architecture with full source inspectability and a guaranteed **Zero-Write Safety Barrier**.

Part of the **ZeroUniverse** application suite, ZRecover is designed for industrial, enterprise, and personal disaster-recovery scenarios.

---

## 🌟 Key Features

- 🛡️ **Zero-Write Safety Barrier**: Hardened barrier strictly prohibits restoring or writing recovered data onto the source volume being scanned, eliminating accidental sector overwrites and data corruption.
- 🔍 **Deep NTFS MFT Parsing**: Directly parses Master File Table (`$MFT`) records, `$STANDARD_INFORMATION`, `$FILE_NAME`, and non-resident data runs to reconstruct original folder hierarchies and file creation timestamps.
- ⚡ **Deep Raw Sector Carving**: High-throughput file carver reading directly across sector boundaries to extract fragmented files when the file system index is missing or wiped:
  - **Images**: JPEG (SOF0/SOF2 marker verification), PNG (IHDR/IEND chunk validation).
  - **Documents**: PDF (linearized trailer and cross-reference stream parser), Office/ZIP OpenXML (EOCD validation).
  - **Databases**: SQLite / embedded database headers and page validation.
  - **Media**: MP4 / ISO-BMFF atom box carver (`ftyp`, `moov`, `mdat`).
- 📊 **Shannon Entropy Analysis**: Real-time entropy calculation across candidate file buffers to distinguish between encrypted/compressed data streams and plain executable or document payloads.
- 🕰️ **VSS Snapshot Explorer**: Discovers and accesses Windows Volume Shadow Copy (VSS) snapshots to restore previous versions of files captured prior to deletion or ransomware activity.
- 🗑️ **Recycle Bin Metadata Parser**: Analyzes `$I...` and `$R...` metadata index entries to restore files deleted through Windows Explorer along with their original filenames and deletion timestamps.
- 👁️ **Dual-Tab Inspector & Visual Preview**:
  - **Visual Preview Tab**: Instant in-memory image rendering for supported graphical formats (PNG, JPG, BMP).
  - **Hex / Metadata Inspector**: Deep binary view with offset inspection and file structure sanity checks.
- 🗂️ **Structure-Preserving Restore**: Rebuilds the recovered directory tree hierarchy cleanly at the selected destination folder.
- 🚀 **Dual Execution Modes**: Headless CLI runner for rapid automation and modern WPF GUI with full cancellation and elevation indicators.

---

## 🏗 Project Layout

| Component | Path | Description |
| :--- | :--- | :--- |
| **`ZRecover.Core`** | `src/ZRecover.Core/` | Recovery engine: raw sector readers, NTFS MFT parser, carvers, Shannon entropy, VSS explorer, safety barriers |
| **`ZRecover.UI`** | `src/ZRecover.UI/` | Modern Windows Presentation Foundation (WPF) GUI with dual-tab preview, capacity visualizer, and candidate DataGrid |
| **`ZRecover.Cli`** | `src/ZRecover.Cli/` | Headless, scriptable command-line interface for automated recovery workflows |
| **`ZRecover.Tests`** | `tests/ZRecover.Tests/` | Comprehensive xUnit unit and integration test suite |

---

## 💻 Usage

### Graphical Desktop Application

Launch `ZRecover.exe` (run as Administrator for raw physical drive access and direct MFT scanning).
1. **Select Target**: Choose a logical drive (e.g. `C:\`, `D:\`) or a specific folder for targeted recovery.
2. **Scan**: Click **Scan** to initiate parallel MFT analysis and sector carving.
3. **Inspect & Preview**: Browse candidate files by category (Pictures, Documents, Archives, Media, Code), inspect file metadata, or view image previews.
4. **Restore**: Choose a destination on a different drive and recover selected files with structure preservation.

### Command Line Interface (CLI)

```bash
# Scan a volume and output recovered candidates to directory
zrecover scan --drive D: --output E:\Restored --carve

# Scan a specific directory for user-mode recovery fallback
zrecover scan --path "D:\Projects\LostData" --output "E:\Recovered"

# List Volume Shadow Copies
zrecover vss list --drive C:
```

---

## 🔨 Build from Source

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Windows 10/11 (x64) with administrative privileges for direct disk I/O

### Build Commands

```bash
# Build complete solution in Release configuration
dotnet build ZRecover.slnx -c Release

# Run test suite
dotnet test tests/ZRecover.Tests/ZRecover.Tests.csproj

# Produce single-file deployment package
pwsh ./publish.ps1
```

---

## 📄 License

Licensed under the **MIT License**. Part of the sovereign **ZeroUniverse** industrial computing ecosystem.
