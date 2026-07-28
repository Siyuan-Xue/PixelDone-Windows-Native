# PixelDone for Windows

PixelDone's forward-looking Windows client. This repository is a clean native rewrite, not a
rename or continuation of the legacy Tauri application.

## Platform contract

- Windows 11 25H2 or newer (build 26200+), x64 only
- C# 14 and .NET 10 LTS
- WinUI 3 on Windows App SDK 2.3.1
- Unpackaged, self-contained deployment
- Per-user NSIS installer; no MSIX identity, Store dependency, or Authenticode requirement
- Local-first SQLite data under `%LOCALAPPDATA%\PixelDone\pixeldone.db`
- PixelDone cloud schema 3.2 is the compatibility boundary

Local files from the legacy Tauri client are intentionally not imported. A later cloud slice
will restore a signed-in user's state from Supabase.

## Official template provenance

The app project was generated with Microsoft's official Windows App SDK templates:

```powershell
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
dotnet new winui `
  -n PixelDone.Windows `
  -o src/PixelDone.Windows `
  --dotnet-version net10.0 `
  --windowsAppSdkVersion 2.3.1
```

The generated project was then narrowed to an unpackaged, self-contained x64 application.
Core, infrastructure, tests, packaging, and product UI are PixelDone-specific layers around
that official scaffold.

## Repository boundaries

This directory is a sibling of `PixelDone`, `PixelDone-windows`, and `PixelDone-Linux`.
It owns all of its source, NuGet caches through the normal user cache, build outputs, tests,
and release artifacts. It never imports source files by relative path from another PixelDone
repository.

```text
PixelDone-Windows-Native/
├── src/
│   ├── PixelDone.Core/            # framework-free rules and cloud-compatible models
│   ├── PixelDone.Infrastructure/  # Windows SQLite adapter
│   └── PixelDone.Windows/         # WinUI 3 app
├── tests/PixelDone.Core.Tests/
├── packaging/PixelDone.nsi
├── scripts/build.ps1
└── artifacts/                     # ignored, repository-local outputs
```

## Build

```powershell
.\scripts\build.ps1
```

To build the installer after installing NSIS:

```powershell
.\scripts\build.ps1 -Installer
```

The first implemented vertical slice supports creating, sorting, hiding, completing, editing,
deleting, and persistently reloading tasks. Trash, settings, notifications, images, and cloud
sync remain explicit follow-up slices.
