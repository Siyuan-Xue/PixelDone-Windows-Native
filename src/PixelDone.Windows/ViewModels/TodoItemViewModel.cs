using CommunityToolkit.Mvvm.ComponentModel;
using PixelDone.Core;

namespace PixelDone.Windows.ViewModels;

public sealed class TodoItemViewModel(
    TodoItem model,
    bool showDdl = false,
    bool showQuickDelete = false) : ObservableObject
{
    public TodoItem Model { get; private set; } = model;

    public string Id => Model.Id;
    public string Title => Model.Title;
    public string PriorityLabel => Model.Priority == TodoPriority.XHigh
        ? "X-HIGH"
        : Model.Priority.ToString().ToUpperInvariant();
    public string DueDisplay
    {
        get
        {
            if (Model.DueAtMillis <= 0)
            {
                return "NO DEADLINE";
            }

            var due = DateTimeOffset.FromUnixTimeMilliseconds(Model.DueAtMillis).ToLocalTime();
            if (!showDdl)
            {
                return due.ToString("yyyy-MM-dd  HH:mm");
            }

            var remaining = due - DateTimeOffset.Now;
            return remaining <= TimeSpan.Zero
                ? $"OVERDUE · {FormatDuration(remaining.Duration())}"
                : $"DDL · {FormatDuration(remaining)}";
        }
    }

    public string RepeatDisplay => Model.ReminderRepeat == ReminderRepeat.None
        ? string.Empty
        : $" · {Model.ReminderRepeat.ToString().ToUpperInvariant()}";
    public string CompletionGlyph => Model.Completed ? "✓" : string.Empty;
    public double ContentOpacity => Model.Completed ? 0.46 : 1.0;
    public Microsoft.UI.Xaml.Visibility QuickDeleteVisibility =>
        showQuickDelete
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility PriorityVisibility =>
        showQuickDelete
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
    public string TrashOrigin =>
        Model.TrashedFromChecklistName is { Length: > 0 } name ? $"FROM · {name}" : string.Empty;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}D {duration.Hours}H";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}H {duration.Minutes}M"
            : $"{Math.Max(0, duration.Minutes)}M";
    }
}
