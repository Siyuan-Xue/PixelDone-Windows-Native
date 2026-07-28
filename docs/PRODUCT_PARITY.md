# PixelDone 4 Windows product parity

- Status: Beta implementation complete; production service validation pending
- Android behavior reference: PixelDone 3.3.6
- Product version: 4.0.0-beta.1
- Cloud contract: 3.2

## Implemented parity

The native client implements the Android product domain for checklists, tasks, priorities,
deadlines, completion, sorting, repeat rules, Trash, restore, permanent deletion, attachment
metadata, language settings, Supabase records, tombstones, mutations, cursors, realtime
invalidations, persistent conflicts, and first-sign-in cloud restore.

The desktop UI exposes native equivalents for the configurable Dock, quick delete,
completed-task cleanup, transaction-scoped batch deletion, Markdown export, image picking and
preview, account lifecycle, password change, manual sync, conflict review, themes, languages,
update discovery, and reminder delivery.

## Intentional platform mappings

| Android behavior | Windows 11 behavior |
| --- | --- |
| Room | Microsoft.Data.Sqlite over Windows SQLite |
| Android Keystore | Windows Credential Manager |
| AlarmManager/WorkManager | Per-user Task Scheduler entry |
| Android notification/full-screen XHigh alarm | Windows App Notification, urgent scenario |
| Photo Picker/FileProvider | FileOpenPicker and app-private file cache |
| APK update/install handoff | NSIS release asset and Windows shell handoff |
| Home-screen widget | Not included by explicit product decision |

## Release gates not satisfiable from this repository alone

- Run sign-up, first-sign-in destructive restore, bidirectional sync, realtime invalidation,
  attachment upload/download/cleanup, password change, and conflict resolution against the
  production Supabase 3.2 deployment.
- Test update discovery against the published GitHub beta release containing the exact
  `PixelDone-4.0.0-beta.1-win-x64-setup.exe` asset. Gitee publishing is intentionally out of
  scope.
- Add Authenticode signing before a public stable release if Windows reputation warnings are
  unacceptable. Signing is not required for the selected beta deployment model.

These are deployment/service gates, not missing local implementation. Until they pass, the
correct claim is “feature-complete beta,” not “production-complete stable.”
