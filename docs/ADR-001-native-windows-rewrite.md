# ADR 001: Native Windows rewrite

- Status: Accepted
- Date: 2026-07-28

## Decision

Build PixelDone for Windows as a new WinUI 3 application using C# 14, .NET 10 LTS, and the
current stable Windows App SDK. Target Windows 11 25H2+ on x64 and publish an unpackaged,
self-contained application with a per-user NSIS installer.

## Why this is a full rewrite

The legacy client is organized around Tauri, Svelte, a WebView-rendered UI, and Rust commands.
WinUI owns composition, navigation, windowing, app lifecycle, notifications, accessibility,
and XAML binding differently. Retaining that shell would preserve the abstraction boundary we
are deliberately removing.

Business behavior is ported against Android 3.3.6, desktop 3.3.1, and cloud schema 3.2.
Tauri UI and Windows adapter code are reference material, not dependencies.

## Consequences

- Native Windows behavior can move with the Windows App SDK without a web compatibility layer.
- The Windows and Linux clients have separate presentation and operating-system adapters.
- Cloud schema 3.2, not a shared GUI framework, is the cross-platform contract.
- PixelDone 4.0 is a product version, not a local- or cloud-schema migration. The installer
  removes a detected Tauri installation and its local data instead of importing old SQLite,
  WebView, attachment-cache, or credential state.
- The native client stores its clean baseline under
  `%LOCALAPPDATA%\com.milesxue.pixeldone.windows\data\pixeldone.sqlite3`; it never writes
  user data into either the legacy `%LOCALAPPDATA%\PixelDone` program directory or the new
  `%LOCALAPPDATA%\Programs\PixelDone` program directory.
- Authenticated users restore their state from Supabase schema 3.2 with an initial cursor-zero
  pull. Users may also start a fresh local-only workspace before signing in.
- Unsigned installers show normal Windows publisher/reputation warnings until signing is added.
- `AppNotificationManager` remains optional at runtime as Microsoft recommends for an
  unpackaged self-contained app. The 2.3.1 publish omission of its Insights resource is filled
  deterministically from the signed official runtime MSIX during `scripts/build.ps1`.
- The beta is feature-complete locally, but production Supabase 3.2 and published-release
  update flows remain external release gates documented in `PRODUCT_PARITY.md`.
