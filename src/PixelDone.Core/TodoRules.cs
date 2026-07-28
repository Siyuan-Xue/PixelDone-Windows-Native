namespace PixelDone.Core;

public static class TodoRules
{
    private const long DailyIntervalMillis = 24L * 60L * 60L * 1000L;
    private const long WeeklyIntervalMillis = 7L * DailyIntervalMillis;

    public static TodoItem? Create(
        string id,
        string titleInput,
        TodoPriority priority,
        long dueAtMillis,
        long nowMillis,
        ReminderRepeat reminderRepeat = ReminderRepeat.None)
    {
        var title = titleInput.Trim();
        return title.Length == 0
            ? null
            : new TodoItem(
                id,
                PixelDoneChecklists.MainId,
                title,
                priority,
                dueAtMillis,
                false,
                nowMillis,
                nowMillis,
                reminderRepeat);
    }

    public static IReadOnlyList<TodoItem> Visible(
        IEnumerable<TodoItem> items,
        TodoSortMode sortMode,
        bool hideCompleted)
    {
        var visible = hideCompleted ? items.Where(item => !item.Completed) : items;
        return sortMode switch
        {
            TodoSortMode.Time => visible
                .OrderBy(item => item.DueAtMillis == 0 ? long.MaxValue : item.DueAtMillis)
                .ThenBy(item => item.CreatedAtMillis)
                .ToArray(),
            _ => visible
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.DueAtMillis == 0 ? long.MaxValue : item.DueAtMillis)
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
        if (item.Completed || item.DueAtMillis <= 0)
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
}
