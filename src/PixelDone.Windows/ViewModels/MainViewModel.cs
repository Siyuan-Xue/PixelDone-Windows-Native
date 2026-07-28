using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PixelDone.Core;

namespace PixelDone.Windows.ViewModels;

public sealed partial class MainViewModel(ITodoRepository repository) : ObservableObject
{
    private readonly List<TodoItem> _allItems = [];

    public ObservableCollection<TodoItemViewModel> Todos { get; } = [];
    public IReadOnlyList<TodoPriority> Priorities { get; } = Enum.GetValues<TodoPriority>();
    public IReadOnlyList<ReminderRepeat> RepeatOptions { get; } = Enum.GetValues<ReminderRepeat>();
    public IReadOnlyList<TodoSortMode> SortOptions { get; } = Enum.GetValues<TodoSortMode>();

    [ObservableProperty]
    public partial string NewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TodoPriority NewPriority { get; set; } = TodoPriority.Medium;

    [ObservableProperty]
    public partial bool HideCompleted { get; set; }

    [ObservableProperty]
    public partial TodoSortMode SortMode { get; set; } = TodoSortMode.Priority;

    [ObservableProperty]
    public partial TodoItemViewModel? SelectedTodo { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TodoPriority EditorPriority { get; set; } = TodoPriority.Medium;

    [ObservableProperty]
    public partial ReminderRepeat EditorRepeat { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset EditorDueDate { get; set; } = DateTimeOffset.Now.Date.AddDays(1);

    [ObservableProperty]
    public partial TimeSpan EditorDueTime { get; set; } = new(9, 0, 0);

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "LOCAL · READY";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string OpenCountLabel =>
        $"{_allItems.Count(item => !item.Completed)} OPEN · {_allItems.Count} TOTAL";

    public bool HasSelection => SelectedTodo is not null;

    partial void OnHideCompletedChanged(bool value) => RefreshVisibleItems();

    partial void OnSortModeChanged(TodoSortMode value) => RefreshVisibleItems();

    partial void OnSelectedTodoChanged(TodoItemViewModel? value)
    {
        if (value is not null)
        {
            EditorTitle = value.Model.Title;
            EditorPriority = value.Model.Priority;
            EditorRepeat = value.Model.ReminderRepeat;
            var due = value.Model.DueAtMillis > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(value.Model.DueAtMillis).ToLocalTime()
                : DateTimeOffset.Now.Date.AddDays(1).AddHours(9);
            EditorDueDate = due;
            EditorDueTime = due.TimeOfDay;
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    public async Task InitializeAsync()
    {
        await ExecuteAsync(async () =>
        {
            await repository.InitializeAsync();
            await ReloadAsync();
            StatusMessage = "LOCAL · SQLITE · READY";
        });
    }

    public async Task AddTodoAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var due = new DateTimeOffset(DateTime.Now.AddDays(1).Date.AddHours(9))
            .ToUniversalTime()
            .ToUnixTimeMilliseconds();
        var item = TodoRules.Create(
            Guid.NewGuid().ToString("N"),
            NewTitle,
            NewPriority,
            due,
            now);

        if (item is null)
        {
            StatusMessage = "TYPE A TASK FIRST";
            return;
        }

        await ExecuteAsync(async () =>
        {
            await repository.UpsertAsync(item);
            NewTitle = string.Empty;
            await ReloadAsync(item.Id);
            StatusMessage = "TASK CREATED";
        });
    }

    public async Task ToggleAsync(TodoItemViewModel todo)
    {
        var updated = todo.Model with
        {
            Completed = !todo.Model.Completed,
            UpdatedAtMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        await ExecuteAsync(async () =>
        {
            await repository.UpsertAsync(updated);
            await ReloadAsync(updated.Id);
            StatusMessage = updated.Completed ? "TASK COMPLETED" : "TASK REOPENED";
        });
    }

    public async Task SaveSelectedAsync()
    {
        if (SelectedTodo is null)
        {
            return;
        }

        var localDue = new DateTimeOffset(EditorDueDate.Date + EditorDueTime);
        var updated = TodoRules.Update(
            SelectedTodo.Model,
            EditorTitle,
            EditorPriority,
            localDue.ToUniversalTime().ToUnixTimeMilliseconds(),
            EditorRepeat,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (updated is null)
        {
            StatusMessage = "TITLE CANNOT BE EMPTY";
            return;
        }

        await ExecuteAsync(async () =>
        {
            await repository.UpsertAsync(updated);
            await ReloadAsync(updated.Id);
            StatusMessage = "CHANGES SAVED";
        });
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedTodo is null)
        {
            return;
        }

        var id = SelectedTodo.Id;
        await ExecuteAsync(async () =>
        {
            await repository.DeleteAsync(id);
            await ReloadAsync();
            StatusMessage = "TASK DELETED";
        });
    }

    private async Task ReloadAsync(string? selectedId = null)
    {
        selectedId ??= SelectedTodo?.Id;
        _allItems.Clear();
        _allItems.AddRange(await repository.ListAsync(PixelDoneChecklists.MainId));
        RefreshVisibleItems(selectedId);
        OnPropertyChanged(nameof(OpenCountLabel));
    }

    private void RefreshVisibleItems(string? selectedId = null)
    {
        selectedId ??= SelectedTodo?.Id;
        var visible = TodoRules.Visible(_allItems, SortMode, HideCompleted);
        Todos.Clear();
        foreach (var item in visible)
        {
            Todos.Add(new TodoItemViewModel(item));
        }

        SelectedTodo = Todos.FirstOrDefault(todo => todo.Id == selectedId);
        OnPropertyChanged(nameof(OpenCountLabel));
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = $"ERROR · {exception.Message.ToUpperInvariant()}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
