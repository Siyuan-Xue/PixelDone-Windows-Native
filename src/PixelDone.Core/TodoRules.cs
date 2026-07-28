namespace PixelDone.Core;

using System.Text;

public enum MarkdownExportMode
{
    Simple,
    Detailed,
}

public static class DockRules
{
    public const int MaxActions = 4;

    public static IReadOnlyList<DockAction> All { get; } =
    [
        DockAction.Sort,
        DockAction.Ddl,
        DockAction.HideDone,
        DockAction.CleanDone,
        DockAction.QuickDelete,
        DockAction.BatchDelete,
        DockAction.ExportMarkdown,
    ];

    public static IReadOnlyList<DockAction> Normalize(IEnumerable<DockAction> actions) =>
        actions
            .Where(All.Contains)
            .Distinct()
            .Take(MaxActions)
            .ToArray();

    public static IReadOnlyList<DockAction> Toggle(
        IEnumerable<DockAction> actions,
        DockAction action)
    {
        var normalized = Normalize(actions).ToList();
        if (normalized.Remove(action))
        {
            return normalized;
        }

        if (normalized.Count == MaxActions)
        {
            normalized.RemoveAt(0);
        }

        normalized.Add(action);
        return normalized;
    }

    public static IReadOnlyList<DockAction> Move(
        IEnumerable<DockAction> actions,
        DockAction action,
        int offset)
    {
        var normalized = Normalize(actions).ToList();
        var from = normalized.IndexOf(action);
        if (from < 0)
        {
            return normalized;
        }

        var to = Math.Clamp(from + offset, 0, normalized.Count - 1);
        if (from != to)
        {
            normalized.RemoveAt(from);
            normalized.Insert(to, action);
        }

        return normalized;
    }
}

public static class TodoRules
{
    public const long DailyIntervalMillis = 24L * 60L * 60L * 1000L;
    public const long WeeklyIntervalMillis = 7L * DailyIntervalMillis;

    public static TodoItem? Create(
        string id,
        string titleInput,
        TodoPriority priority,
        long dueAtMillis,
        long nowMillis,
        ReminderRepeat reminderRepeat = ReminderRepeat.None,
        string checklistId = PixelDoneChecklists.MainId,
        int sortIndex = 0)
    {
        var title = titleInput.Trim();
        return title.Length == 0
            ? null
            : new TodoItem(
                id,
                checklistId,
                title,
                priority,
                dueAtMillis,
                false,
                nowMillis,
                nowMillis,
                reminderRepeat,
                SortIndex: sortIndex);
    }

    public static IReadOnlyList<TodoItem> Visible(
        IEnumerable<TodoItem> items,
        TodoSortMode sortMode,
        bool hideCompleted)
    {
        var visible = items.Where(item => !item.IsTrashed);
        visible = hideCompleted ? visible.Where(item => !item.Completed) : visible;
        return sortMode switch
        {
            TodoSortMode.Time => visible
                .OrderBy(item => item.DueAtMillis == 0 ? long.MaxValue : item.DueAtMillis)
                .ThenBy(item => item.SortIndex)
                .ThenBy(item => item.CreatedAtMillis)
                .ToArray(),
            _ => visible
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.DueAtMillis == 0 ? long.MaxValue : item.DueAtMillis)
                .ThenBy(item => item.SortIndex)
                .ThenBy(item => item.CreatedAtMillis)
                .ToArray(),
        };
    }

    public static TodoItem? Update(
        TodoItem item,
        string titleInput,
        TodoPriority priority,
        long dueAtMillis,
        ReminderRepeat repeat,
        long nowMillis)
    {
        var title = titleInput.Trim();
        return title.Length == 0
            ? null
            : item with
            {
                Title = title,
                Priority = priority,
                DueAtMillis = dueAtMillis,
                ReminderRepeat = repeat,
                UpdatedAtMillis = nowMillis,
            };
    }

    public static long? NextReminderAt(TodoItem item, long nowMillis)
    {
        if (item.Completed || item.IsTrashed || item.DueAtMillis <= 0)
        {
            return null;
        }

        var interval = item.ReminderRepeat switch
        {
            ReminderRepeat.Daily => DailyIntervalMillis,
            ReminderRepeat.Weekly => WeeklyIntervalMillis,
            _ => 0,
        };

        if (interval == 0)
        {
            return item.DueAtMillis > nowMillis ? item.DueAtMillis : null;
        }

        if (item.DueAtMillis > nowMillis)
        {
            return item.DueAtMillis;
        }

        var elapsed = nowMillis - item.DueAtMillis;
        return item.DueAtMillis + ((elapsed / interval) + 1) * interval;
    }

    public static string ExportMarkdown(
        TodoChecklist checklist,
        IEnumerable<TodoItem> items,
        TodoSortMode sortMode,
        MarkdownExportMode mode = MarkdownExportMode.Detailed)
    {
        var builder = new StringBuilder()
            .Append("# ")
            .AppendLine(EscapeMarkdown(checklist.Name))
            .AppendLine();
        foreach (var item in Visible(items, sortMode, false))
        {
            builder
                .Append("- [")
                .Append(item.Completed ? 'x' : ' ')
                .Append("] ")
                .Append(EscapeMarkdown(item.Title));
            if (mode == MarkdownExportMode.Detailed)
            {
                builder
                    .AppendLine()
                    .Append("  - Priority: ")
                    .AppendLine(item.Priority.ToString().ToUpperInvariant())
                    .Append("  - Due: ")
                    .AppendLine(
                        item.DueAtMillis > 0
                            ? DateTimeOffset
                                .FromUnixTimeMilliseconds(item.DueAtMillis)
                                .ToLocalTime()
                                .ToString("yyyy-MM-dd HH:mm")
                            : "None")
                    .Append("  - Repeat: ")
                    .Append(item.ReminderRepeat.ToString().ToUpperInvariant());
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeMarkdown(string value)
    {
        ReadOnlySpan<char> special = "\\`*_{}[]()#+-.!>|~";
        var flattened = string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        var builder = new StringBuilder(flattened.Length);
        foreach (var character in flattened)
        {
            if (special.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static TodoItem MoveToTrash(
        TodoItem item,
        string checklistName,
        long nowMillis) =>
        item with
        {
            ChecklistId = PixelDoneChecklists.TrashId,
            TrashedFromChecklistId = item.ChecklistId,
            TrashedFromChecklistName = checklistName,
            TrashedAtMillis = nowMillis,
            UpdatedAtMillis = nowMillis,
            SyncState = SyncState.Dirty,
        };

    public static TodoItem RestoreFromTrash(
        TodoItem item,
        string restoredChecklistId,
        long nowMillis) =>
        item with
        {
            ChecklistId = restoredChecklistId,
            TrashedFromChecklistId = null,
            TrashedFromChecklistName = null,
            TrashedAtMillis = null,
            UpdatedAtMillis = nowMillis,
            SyncState = SyncState.Dirty,
        };

    public static bool IsTrashExpired(TodoItem item, long nowMillis)
    {
        const long retentionMillis = 30L * DailyIntervalMillis;
        return item.TrashedAtMillis is { } trashedAt &&
            nowMillis - trashedAt >= retentionMillis;
    }
}
