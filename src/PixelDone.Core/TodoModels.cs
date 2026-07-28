namespace PixelDone.Core;

public enum TodoPriority
{
    XHigh,
    High,
    Medium,
    Low,
}

public enum ReminderRepeat
{
    None,
    Daily,
    Weekly,
}

public enum TodoSortMode
{
    Priority,
    Time,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum LanguageMode
{
    System,
    English,
    SimplifiedChinese,
    Arabic,
    French,
    Russian,
    Spanish,
}

public enum DockAction
{
    Sort,
    Ddl,
    HideDone,
    CleanDone,
    QuickDelete,
    BatchDelete,
    ExportMarkdown,
}

public enum DockPlusPlacement
{
    Center,
    LeftEdge,
    RightEdge,
}

public enum SyncState
{
    LocalOnly,
    Clean,
    Dirty,
    Conflict,
    Deleted,
}

public enum SyncRecordType
{
    Checklist,
    Todo,
    Attachment,
    Settings,
}

public sealed record TodoChecklist(
    string Id,
    string Name,
    int SortIndex,
    long CreatedAtMillis,
    long UpdatedAtMillis,
    SyncState SyncState = SyncState.LocalOnly,
    string? RemoteId = null,
    string? OwnerUserId = null,
    long? RemoteVersion = null,
    long? DeletedAtMillis = null)
{
    public bool IsSystem =>
        Id is PixelDoneChecklists.TrashId or PixelDoneChecklists.SettingsId;
}

public sealed record TodoItem(
    string Id,
    string ChecklistId,
    string Title,
    TodoPriority Priority,
    long DueAtMillis,
    bool Completed,
    long CreatedAtMillis,
    long UpdatedAtMillis,
    ReminderRepeat ReminderRepeat = ReminderRepeat.None,
    string? ImageLocalName = null,
    string? ImageRemotePath = null,
    string? TrashedFromChecklistId = null,
    string? TrashedFromChecklistName = null,
    long? TrashedAtMillis = null,
    int SortIndex = 0,
    SyncState SyncState = SyncState.LocalOnly,
    string? RemoteId = null,
    string? OwnerUserId = null,
    long? RemoteVersion = null,
    long? LastSyncedAtMillis = null,
    string? LastSyncError = null)
{
    public bool IsTrashed => TrashedAtMillis is not null;
}

public sealed record TodoAttachment(
    string Id,
    string TodoId,
    string? LocalPath,
    string? RemotePath,
    string Sha256,
    string MimeType,
    long ByteSize,
    long UpdatedAtMillis,
    SyncState SyncState = SyncState.LocalOnly,
    long? RemoteVersion = null,
    long? DeletedAtMillis = null);

public sealed record PixelDoneSettings(
    ThemeMode Theme = ThemeMode.System,
    LanguageMode Language = LanguageMode.System,
    TodoSortMode SortMode = TodoSortMode.Priority,
    bool ShowDdl = true,
    bool HideCompleted = false,
    bool QuickDelete = false,
    bool UpdatePrompts = true,
    bool EnhancedXHighAlarm = false,
    IReadOnlyList<DockAction>? DockActions = null,
    DockPlusPlacement DockPlusPlacement = DockPlusPlacement.Center)
{
    public IReadOnlyList<DockAction> EffectiveDockActions =>
        DockRules.Normalize(DockActions ?? [DockAction.Sort, DockAction.Ddl]);
}

public sealed record SyncConflict(
    string Id,
    SyncRecordType RecordType,
    string LocalId,
    string FieldsJson,
    string LocalJson,
    string CloudJson,
    long CreatedAtMillis,
    long? ResolvedAtMillis = null);

public sealed record SyncTombstone(
    string Id,
    SyncRecordType RecordType,
    string LocalId,
    long DeletedAtMillis,
    long? RemoteVersion = null,
    SyncState SyncState = SyncState.Dirty);

public static class PixelDoneChecklists
{
    public const string MainId = "main";
    public const string MainName = "MAIN";
    public const string TrashId = "trash";
    public const string TrashName = "TRASH";
    public const string SettingsId = "settings";
    public const string SettingsName = "SETTINGS";
}
