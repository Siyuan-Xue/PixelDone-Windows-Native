# PixelDone 4 Windows implementation audit

## Repository isolation

`PixelDone-Windows-Native` is a standalone Git repository and a sibling of `PixelDone`,
`PixelDone-windows`, and `PixelDone-Linux`. It contains no relative source links to those
repositories. Build output is restricted to ignored `bin/`, `obj/`, and `artifacts/`
directories.

## Added product areas

- `src/PixelDone.Core`: product version, complete local/cloud models, Dock/export/reminder
  rules, and repository contracts
- `src/PixelDone.Infrastructure`: SQLite implementation, Supabase 3.2 client/contracts,
  sync engine, and release update client
- `src/PixelDone.Windows/Services`: Credential Manager, cloud session orchestration,
  attachments, and App Notifications
- `src/PixelDone.Windows/Strings`: six explicit translations plus the English baseline
- `tests/PixelDone.Core.Tests`: domain, update selection, SQLite transaction, restore, sync
  metadata, Trash, and reminder tests
- `packaging` and `scripts`: clean-cut NSIS installation, standard restore/test/publish, and
  deterministic localization generation

## Dependency method

NuGet packages are declared in project files and restored with normal `dotnet restore`.
The build uses the normal user NuGet cache, exactly as a manual command does; it is not a
sandbox-only dependency copy. .NET 10, the Windows App SDK templates, and NSIS were installed
with standard machine command-line installers.

Windows App SDK 2.3.1 omits
`Microsoft.WindowsAppRuntime.Insights.Resource.dll` from this unpackaged self-contained
publish shape. The build script deterministically extracts the signed Microsoft file from the
official restored runtime MSIX and verifies it before packaging. No third-party binary is
substituted.

## Conflicts

The V4 installer removes a detected legacy Tauri installation and its local program/data
directory. This is deliberate and matches the approved cloud-authoritative clean cut. The new
program directory and data directory are separate from one another and from every source
repository. No Android, legacy Windows, or Linux source directory is modified by the build.
