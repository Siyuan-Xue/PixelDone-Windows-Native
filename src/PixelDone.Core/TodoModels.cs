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
    long? TrashedAtMillis = null);

public static class PixelDoneChecklists
{
    public const string MainId = "main";
    public const string MainName = "MAIN";
    public const string TrashId = "trash";
    public const string TrashName = "TRASH";
    public const string SettingsId = "settings";
    public const string SettingsName = "SETTINGS";
}
