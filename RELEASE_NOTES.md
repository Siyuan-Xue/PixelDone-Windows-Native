# PixelDone for Windows 4.0.0-beta.1

The first public beta of PixelDone's clean native Windows rewrite.

## Highlights

- Native WinUI 3 interface on .NET 10 and Windows App SDK 2.3.1
- Android 3.3.6 product parity for checklists, tasks, priorities, deadlines, repeat rules,
  Trash, configurable Dock actions, batch deletion, and Markdown export
- Supabase 3.2 authentication, synchronization, realtime invalidation, attachments, conflicts,
  and cloud-authoritative first sign-in restore
- Windows Credential Manager, App Notifications, Task Scheduler reminders, native picker,
  system preview, multilingual resources, themes, and Arabic right-to-left layout
- Self-contained per-user NSIS installation with a clean replacement of the legacy Tauri client

## Requirements

- Windows 11 25H2 or newer
- x64 processor

This beta installer is not Authenticode-signed, so Windows may display publisher or reputation
warnings. Local data from the legacy desktop client is intentionally not migrated; sign in to
restore cloud data.
