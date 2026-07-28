using CommunityToolkit.Mvvm.ComponentModel;
using PixelDone.Core;

namespace PixelDone.Windows.ViewModels;

public sealed class TodoItemViewModel(TodoItem model) : ObservableObject
{
    public TodoItem Model { get; private set; } = model;

    public string Id => Model.Id;
    public string Title => Model.Title;
    public string PriorityLabel => Model.Priority == TodoPriority.XHigh
        ? "X-HIGH"
        : Model.Priority.ToString().ToUpperInvariant();
    public string DueDisplay => Model.DueAtMillis <= 0
        ? "NO DEADLINE"
        : DateTimeOffset.FromUnixTimeMilliseconds(Model.DueAtMillis)
            .ToLocalTime()
            .ToString("yyyy-MM-dd  HH:mm");
    public string RepeatDisplay => Model.ReminderRepeat == ReminderRepeat.None
        ? string.Empty
        : $" · {Model.ReminderRepeat.ToString().ToUpperInvariant()}";
    public string CompletionGlyph => Model.Completed ? "✓" : string.Empty;
    public double ContentOpacity => Model.Completed ? 0.46 : 1.0;
}
