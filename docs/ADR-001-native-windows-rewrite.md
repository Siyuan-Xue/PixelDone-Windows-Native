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
- Existing Tauri local data is not migrated; authenticated users will restore from cloud.
- Unsigned installers show normal Windows publisher/reputation warnings until signing is added.
