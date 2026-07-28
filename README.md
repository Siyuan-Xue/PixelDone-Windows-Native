# PixelDone for Windows

[![CI](https://github.com/Siyuan-Xue/PixelDone-Windows-Native/actions/workflows/ci.yml/badge.svg)](https://github.com/Siyuan-Xue/PixelDone-Windows-Native/actions/workflows/ci.yml)

PixelDone's forward-looking Windows client. This repository is a clean native rewrite, not a
rename or continuation of the legacy Tauri application.

## Platform contract

- Windows 11 25H2 or newer (build 26200+), x64 only
- C# 14 and .NET 10 LTS
- WinUI 3 on Windows App SDK 2.3.1
- Unpackaged, self-contained deployment
- Per-user NSIS installer; no MSIX identity, Store dependency, or Authenticode requirement
- Local-first SQLite data under
  `%LOCALAPPDATA%\com.milesxue.pixeldone.windows\data\pixeldone.sqlite3`
- PixelDone cloud schema 3.2 is the compatibility boundary

PixelDone 4.0 is the product version of this clean native rewrite, not a database migration.
A detected legacy Tauri installation and its local data are removed, and no legacy SQLite,
WebView, cache, attachment, or credential state is imported. New native data lives at
`%LOCALAPPDATA%\com.milesxue.pixeldone.windows\data\pixeldone.sqlite3`. A signed-in user
restores cloud state from Supabase schema 3.2 with a cursor-zero pull.

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
+-- src/
|   +-- PixelDone.Core/            # framework-free rules and cloud models
|   +-- PixelDone.Infrastructure/  # SQLite, Supabase, sync, and update adapters
|   `-- PixelDone.Windows/         # WinUI 3 application and Windows services
+-- tests/PixelDone.Core.Tests/
+-- packaging/PixelDone.nsi
+-- scripts/
`-- artifacts/                     # ignored, repository-local outputs
```

## Product scope implemented

- Multiple checklists, session history, task CRUD, move, completion, priority/time sorting,
  DDL countdown, daily/weekly repeat, and exact 30-day Trash retention
- Configurable Dock with centered/edge `+`, fifth-selection replacement, `QUICK DELETE`,
  `CLEAN DONE`, transaction-scoped batch deletion, and simple/detailed Markdown export
- JPEG, PNG, and WebP attachments up to 10 MiB through the native Windows picker, app-private
  storage/cache, hash validation, system preview, and private Supabase Storage synchronization
- SQLite local-first storage with mutation IDs, tombstones, cursor metadata, persistent
  conflicts, first-sign-in cursor-zero cloud restore, realtime invalidation, and manual sync
- Supabase sign-up/sign-in/sign-out, Credential Manager session storage, verified password
  change with global sign-out, and keep-local/keep-cloud conflict resolution
- Windows App Notifications delivered by a per-user Scheduled Task, including urgent XHigh
  presentation and atomic advancement of repeating deadlines after successful delivery
- System/light/dark themes; System, English, Simplified Chinese, Arabic, French, Russian, and
  Spanish resources; native language names; Arabic right-to-left layout
- GitHub Release discovery with Gitee fallback and channel-aware x64 NSIS asset selection
- Native confirmation dialogs for destructive operations and a self-contained per-user NSIS
  installer

The Android home-screen widget has no Windows counterpart by product decision. Android-only
permission screens, haptics, full-screen alarm activity, and APK installation are represented
by Windows-native notification, picker, credential, and installer behavior instead of being
copied literally. See `docs/PRODUCT_PARITY.md` for the verification boundary.

## Build

```powershell
.\scripts\build.ps1
```

To build the installer after installing NSIS:

```powershell
.\scripts\build.ps1 -Installer
```

The release artifact is
`artifacts\installer\PixelDone-4.0.0-beta.1-win-x64-setup.exe`.

Cloud support is enabled at process launch with:

```powershell
$env:PIXELDONE_SUPABASE_URL = 'https://your-project.supabase.co'
$env:PIXELDONE_SUPABASE_PUBLISHABLE_KEY = 'your-publishable-key'
```

Plain HTTP is rejected unless `PIXELDONE_ALLOW_INSECURE_HTTP=true` is explicitly set for a
trusted development deployment. The server must already satisfy PixelDone cloud schema 3.2.
