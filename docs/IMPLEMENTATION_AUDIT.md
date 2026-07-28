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

## Continuous integration

`.github/workflows/ci.yml` runs on every push, pull request, and manual dispatch. A
`windows-latest` runner installs .NET 10.0.302 and NSIS, verifies `dotnet format`, executes the
repository build script and its 16 tests, builds the self-contained x64 installer, verifies its
versioned filename and size, writes a SHA-256 sidecar, and uploads both files as a seven-day
workflow artifact. Workflow permissions are read-only and duplicate runs are cancelled.

Tag-driven releases are separate from CI. `.github/workflows/release-windows.yml` accepts only
an immutable `vX.Y.Z` or `vX.Y.Z-beta.N` tag reachable from `main`, requires every product and
packaging version declaration to match, rebuilds and re-tests the tagged source, and publishes
the installer plus SHA-256 through an idempotent GitHub Release publisher.

## Conflicts

The V4 installer removes a detected legacy Tauri installation and its local program/data
directory. This is deliberate and matches the approved cloud-authoritative clean cut. The new
program directory and data directory are separate from one another and from every source
repository. No Android, legacy Windows, or Linux source directory is modified by the build.
