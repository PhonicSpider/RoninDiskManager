<p align="center">
  <img src="RoninDiskManager/Assets/icon.png" width="120" alt="Ronin Disk Manager" />
</p>

<h1 align="center">Ronin Disk Manager</h1>

<p align="center">
  A fast, NTFS-native disk usage analyzer and bulk file manager for Windows.<br/>
  Built for the <strong>Ronin Empire</strong> community.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-blue"/>
  <img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0-red"/>
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-purple"/>
</p>

---

## Overview

Ronin Disk Manager scans your drives, visualizes directory sizes in a tree view, and lets you execute bulk **Move** or **Delete** operations on selected items - all backed by PowerShell with real-time console output.

On NTFS drives it reads the Master File Table directly via the USN journal (the same technique WizTree uses), giving near-instant enumeration regardless of how many files are on the volume. Non-NTFS volumes (FAT32, exFAT, USB, network shares) fall back to a fast recursive directory walker automatically.

---

## Features

- **MFT-based scanning** - USN journal enumeration on NTFS for fast, complete disk analysis
- **Automatic fallback** - standard directory walker for FAT32, exFAT, and network drives
- **Tree view** - lazy-loading, virtualized, sorted by size with percentage of root total
- **Move operations** - PowerShell `Move-Item` with Force, WhatIf, Verbose, NeverOverwrite, LiteralPath, and pattern filters
- **Delete operations** - PowerShell `Remove-Item` with Force, Recurse, WhatIf, Verbose, LiteralPath, and pattern filters
- **Dry-run mode** - WhatIf flag on both operations lets you preview changes before committing
- **Real-time console** - live PowerShell stdout/stderr output streamed to the in-app console
- **Ronin theme** - dark crimson, orange, cyan, and black color scheme

---

## Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 / 11 |
| Runtime | [.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Privileges | Administrator (required for MFT access and file operations) |
| Filesystem | NTFS for MFT scanning; FAT32/exFAT/network supported via fallback |

---

## Getting Started

### Download & Run (easiest)

1. Go to the [**Releases**](https://github.com/PhonicSpider/RoninDiskManager/releases/latest) page
2. Download `RoninDiskManager.exe` under the latest release
3. Right-click → **Run as Administrator**

No installation or .NET runtime required — the exe is fully self-contained.

---

### Run from source (for local changes)

Requires [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Clone the repository
git clone https://github.com/PhonicSpider/RoninDiskManager.git
cd RoninDiskManager

# Run (builds automatically)
dotnet run --project RoninDiskManager/RoninDiskManager.csproj
```

> Run your terminal as Administrator, or Windows will prompt for elevation on launch.

### Build your own standalone executable

```powershell
dotnet publish RoninDiskManager/RoninDiskManager.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `RoninDiskManager/bin/Release/net8.0-windows/win-x64/publish/RoninDiskManager.exe`

---

## How It Works

1. **Scan** - Enter a path and click Scan. On NTFS, the engine opens the raw volume (`\\.\C:`), streams all MFT records via `FSCTL_ENUM_USN_DATA`, resolves full paths, and populates file sizes in a parallel directory-batched pass. On other filesystems it falls back to a recursive `EnumerateFileSystemInfos` walk.
2. **Browse** - The tree view shows directories sorted by size. Expand any node to see its children.
3. **Act** - Select a node, choose Move or Delete, configure flags and filters, then click Execute. A confirmation dialog is shown before any delete.
4. **Review** - All PowerShell output appears in the console panel in real time. Use WhatIf for a dry run before committing.

---

## Project Structure

```
RoninDiskManager/
├── Engine/
│   ├── ScanEngine.cs          # Facade — detects NTFS and routes to the right scanner
│   ├── MftScanEngine.cs       # NTFS USN journal scanner (fast path)
│   ├── FallbackScanEngine.cs  # Recursive Win32 directory walker (non-NTFS)
│   └── NativeMethods.cs       # P/Invoke declarations (CreateFile, DeviceIoControl)
├── Models/
│   └── DiskNode.cs            # File/directory tree node
├── ViewModels/
│   ├── MainViewModel.cs       # All state, commands, and PowerShell execution
│   └── DiskNodeViewModel.cs   # Lazy-loading tree node presentation layer
├── Converters/                # WPF value converters
├── Assets/                    # App icon
├── MainWindow.xaml            # UI layout
└── App.xaml                   # Theme and global resources
```

---

## License

Copyright © 2025 Ronin Empire

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but **without any warranty**; without even the implied warranty of merchantability or fitness for a particular purpose. See the [GNU Affero General Public License](LICENSE) for more details.
