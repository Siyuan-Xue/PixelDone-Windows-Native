using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PixelDone.Core;
using PixelDone.Infrastructure;
using PixelDone.Windows.Services;
using Windows.Globalization;

namespace PixelDone.Windows.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ITodoRepository repository;
    private readonly CloudSessionService? _cloud;
    private readonly WindowsAttachmentService _attachmentService;
    private readonly AppUpdateService _updateService;
    private readonly List<TodoItem> _allItems = [];
    private readonly HashSet<string> _batchIds = [];
    private List<DockAction> _dockActions = [];
    private readonly Stack<string> _checklistHistory = [];
    private string? _lastChecklistId;
    private bool _navigatingHistory;
    private PixelDoneSettings _settings = new();
    private bool _initialized;

    public ObservableCollection<ChecklistViewModel> Checklists { get; } = [];
    public ObservableCollection<TodoItemViewModel> Todos { get; } = [];
    public ObservableCollection<ChecklistViewModel> MoveTargets { get; } = [];
    public ObservableCollection<SyncConflict> Conflicts { get; } = [];
    public ObservableCollection<DockItemViewModel> DockItems { get; } = [];
    public ObservableCollection<DockActionChoiceViewModel> DockChoices { get; } = [];
    public IReadOnlyList<TodoPriority> Priorities { get; } = Enum.GetValues<TodoPriority>();
    public IReadOnlyList<ReminderRepeat> RepeatOptions { get; } = Enum.GetValues<ReminderRepeat>();
    public IReadOnlyList<TodoSortMode> SortOptions { get; } = Enum.GetValues<TodoSortMode>();
    public IReadOnlyList<ThemeMode> ThemeOptions { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<LanguageChoiceViewModel> LanguageOptions { get; } =
    [
        new(LanguageMode.System, "System"),
        new(LanguageMode.English, "English"),
        new(LanguageMode.SimplifiedChinese, "简体中文"),
        new(LanguageMode.Arabic, "العربية"),
        new(LanguageMode.French, "Français"),
        new(LanguageMode.Russian, "Русский"),
        new(LanguageMode.Spanish, "Español"),
    ];
    public IReadOnlyList<DockPlusPlacement> DockPlacementOptions { get; } =
        Enum.GetValues<DockPlusPlacement>();

    public MainViewModel(
        ITodoRepository repository,
        CloudSessionService? cloud,
        WindowsAttachmentService attachmentService,
        AppUpdateService updateService,
        string cloudConfigurationMessage)
    {
        this.repository = repository;
        _cloud = cloud;
        _attachmentService = attachmentService;
        _updateService = updateService;
        SelectedLanguageChoice = LanguageOptions[0];
        CloudConfigured = cloud is not null;
        CloudMessage = cloudConfigurationMessage;
        if (_cloud is not null)
        {
            _cloud.StateChanged += OnCloudStateChanged;
        }
    }

    [ObservableProperty]
    public partial ChecklistViewModel? SelectedChecklist { get; set; }

    [ObservableProperty]
    public partial string NewChecklistName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChecklistEditorName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TodoPriority NewPriority { get; set; } = TodoPriority.Medium;

    [ObservableProperty]
    public partial bool HideCompleted { get; set; }

    [ObservableProperty]
    public partial bool ShowDdl { get; set; } = true;

    [ObservableProperty]
    public partial bool QuickDelete { get; set; }

    [ObservableProperty]
    public partial bool UpdatePrompts { get; set; } = true;

    [ObservableProperty]
    public partial bool EnhancedXHighAlarm { get; set; }

    [ObservableProperty]
    public partial TodoSortMode SortMode { get; set; } = TodoSortMode.Priority;

    [ObservableProperty]
    public partial ThemeMode Theme { get; set; } = ThemeMode.System;

    [ObservableProperty]
    public partial LanguageMode Language { get; set; } = LanguageMode.System;

    [ObservableProperty]
    public partial LanguageChoiceViewModel SelectedLanguageChoice { get; set; }

    [ObservableProperty]
    public partial DockPlusPlacement DockPlusPlacement { get; set; } =
        DockPlusPlacement.Center;

    [ObservableProperty]
    public partial bool IsBatchDeleteMode { get; set; }

    [ObservableProperty]
    public partial TodoItemViewModel? SelectedTodo { get; set; }

    [ObservableProperty]
    public partial ChecklistViewModel? SelectedMoveTarget { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TodoPriority EditorPriority { get; set; } = TodoPriority.Medium;

    [ObservableProperty]
    public partial ReminderRepeat EditorRepeat { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset EditorDueDate { get; set; } =
        DateTimeOffset.Now.Date.AddDays(1);

    [ObservableProperty]
    public partial TimeSpan EditorDueTime { get; set; } = new(9, 0, 0);

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "LOCAL · READY";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CloudConfigured { get; set; }

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    public partial string CloudMessage { get; set; }

    [ObservableProperty]
    public partial string CloudEmail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloudPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloudAccount { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AttachmentPath { get; set; }

    [ObservableProperty]
    public partial BitmapImage? AttachmentPreview { get; set; }

    [ObservableProperty]
    public partial string UpdateMessage { get; set; } =
        $"PIXELDONE {PixelDoneProduct.Version}";

    [ObservableProperty]
    public partial Uri? UpdateReleasePage { get; set; }

    public string PageTitle => SelectedChecklist?.Name ?? PixelDoneChecklists.MainName;
    public string OpenCountLabel =>
        IsSettingsPage
            ? "DEVICE PREFERENCES"
            : $"{_allItems.Count(item => !item.Completed)} OPEN · {_allItems.Count} TOTAL";
    public bool HasSelection => SelectedTodo is not null;
    public bool IsTrashPage => SelectedChecklist?.IsTrash == true;
    public bool IsSettingsPage => SelectedChecklist?.IsSettings == true;
    public bool IsTaskPage => !IsSettingsPage;
    public bool CanEditChecklist => SelectedChecklist?.CanEdit == true;
    public Visibility TaskPageVisibility => IsTaskPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsPageVisibility =>
        IsSettingsPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TrashActionVisibility =>
        IsTrashPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalActionVisibility =>
        IsTaskPage && !IsTrashPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CloudSignedInVisibility =>
        IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CloudSignedOutVisibility =>
        CloudConfigured && !IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConflictVisibility =>
        IsSignedIn && Conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AttachmentVisibility =>
        AttachmentPath is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoAttachmentVisibility =>
        HasSelection && AttachmentPath is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BatchDeleteVisibility =>
        IsBatchDeleteMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpdateAvailableVisibility =>
        UpdateReleasePage is not null ? Visibility.Visible : Visibility.Collapsed;
    public ListViewSelectionMode TodoSelectionMode =>
        IsBatchDeleteMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
    public string BatchSelectionLabel => $"{_batchIds.Count} SELECTED";
    public string DeleteActionLabel => IsTrashPage ? "DELETE FOREVER" : "MOVE TO TRASH";
    public bool CanNavigateBack => _checklistHistory.Count > 0;
    public ElementTheme RequestedElementTheme => Theme switch
    {
        ThemeMode.Light => ElementTheme.Light,
        ThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
    public FlowDirection ContentFlowDirection =>
        Language == LanguageMode.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    partial void OnSelectedChecklistChanged(ChecklistViewModel? value)
    {
        if (_initialized && !_navigatingHistory &&
            _lastChecklistId is { } previous &&
            value is not null &&
            previous != value.Id)
        {
            _checklistHistory.Push(previous);
        }
        _lastChecklistId = value?.Id;
        _navigatingHistory = false;
        OnPropertyChanged(nameof(CanNavigateBack));
        IsBatchDeleteMode = false;
        ChecklistEditorName = value?.Name ?? string.Empty;
        RefreshMoveTargets();
        RaisePageState();
        if (_initialized)
        {
            _ = ExecuteAsync(() => ReloadAsync());
        }
    }

    partial void OnHideCompletedChanged(bool value)
    {
        RefreshVisibleItems();
        QueueSettingsSave();
    }

    partial void OnShowDdlChanged(bool value)
    {
        RefreshVisibleItems();
        QueueSettingsSave();
    }

    partial void OnQuickDeleteChanged(bool value)
    {
        RefreshVisibleItems();
        QueueSettingsSave();
    }
    partial void OnUpdatePromptsChanged(bool value) => QueueSettingsSave();
    partial void OnEnhancedXHighAlarmChanged(bool value) => QueueSettingsSave();

    partial void OnSortModeChanged(TodoSortMode value)
    {
        RefreshVisibleItems();
        QueueSettingsSave();
    }

    partial void OnThemeChanged(ThemeMode value)
    {
        OnPropertyChanged(nameof(RequestedElementTheme));
        QueueSettingsSave();
    }
    partial void OnLanguageChanged(LanguageMode value)
    {
        var matchingChoice = LanguageOptions.First(choice => choice.Value == value);
        if (SelectedLanguageChoice != matchingChoice)
        {
            SelectedLanguageChoice = matchingChoice;
        }
        OnPropertyChanged(nameof(ContentFlowDirection));
        if (_initialized)
        {
            ApplicationLanguages.PrimaryLanguageOverride = value switch
            {
                LanguageMode.English => "en",
                LanguageMode.SimplifiedChinese => "zh-Hans",
                LanguageMode.Arabic => "ar",
                LanguageMode.French => "fr",
                LanguageMode.Russian => "ru",
                LanguageMode.Spanish => "es",
                _ => string.Empty,
            };
            StatusMessage = "RESTART PIXELDONE TO APPLY THE LANGUAGE";
        }

        QueueSettingsSave();
    }

    partial void OnSelectedLanguageChoiceChanged(LanguageChoiceViewModel value)
    {
        if (Language != value.Value)
        {
            Language = value.Value;
        }
    }
    partial void OnDockPlusPlacementChanged(DockPlusPlacement value)
    {
        RefreshDock();
        QueueSettingsSave();
    }

    partial void OnIsBatchDeleteModeChanged(bool value)
    {
        if (!value)
        {
            _batchIds.Clear();
        }

        OnPropertyChanged(nameof(BatchDeleteVisibility));
        OnPropertyChanged(nameof(TodoSelectionMode));
        OnPropertyChanged(nameof(BatchSelectionLabel));
        RefreshDock();
    }
    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(CloudSignedInVisibility));
        OnPropertyChanged(nameof(CloudSignedOutVisibility));
        OnPropertyChanged(nameof(ConflictVisibility));
    }

    partial void OnCloudConfiguredChanged(bool value) =>
        OnPropertyChanged(nameof(CloudSignedOutVisibility));

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
        OnPropertyChanged(nameof(NoAttachmentVisibility));
        _ = LoadAttachmentAsync(value?.Id);
    }

    partial void OnAttachmentPathChanged(string? value)
    {
        OnPropertyChanged(nameof(AttachmentVisibility));
        OnPropertyChanged(nameof(NoAttachmentVisibility));
    }

    partial void OnUpdateReleasePageChanged(Uri? value) =>
        OnPropertyChanged(nameof(UpdateAvailableVisibility));

    public async Task InitializeAsync()
    {
        await ExecuteAsync(async () =>
        {
            await repository.InitializeAsync();
            _settings = await repository.GetSettingsAsync();
            ApplySettings(_settings);
            await ReloadChecklistsAsync(PixelDoneChecklists.MainId);
            _initialized = true;
            await ReloadAsync();
            if (_cloud is not null)
            {
                await _cloud.InitializeAsync();
            }
            StatusMessage = "LOCAL · SQLITE · READY";
        });
        if (UpdatePrompts)
        {
            _ = CheckForUpdatesAsync(false);
        }
    }

    public async Task CheckForUpdatesAsync(bool userInitiated = true)
    {
        try
        {
            var result = await _updateService.CheckAsync();
            UpdateMessage = result.Message;
            UpdateReleasePage = result.ReleasePage;
            if (userInitiated || result.State == UpdateState.Available)
            {
                StatusMessage = result.Message;
            }
        }
        catch (Exception exception)
        {
            UpdateMessage = $"UPDATE ERROR · {exception.Message.ToUpperInvariant()}";
            if (userInitiated)
            {
                StatusMessage = UpdateMessage;
            }
        }
    }

    public void NavigateBack()
    {
        while (_checklistHistory.TryPop(out var checklistId))
        {
            var target = Checklists.FirstOrDefault(value => value.Id == checklistId);
            if (target is null || target == SelectedChecklist)
            {
                continue;
            }

            _navigatingHistory = true;
            SelectedChecklist = target;
            break;
        }

        OnPropertyChanged(nameof(CanNavigateBack));
    }

    public Task SignInAsync() =>
        ExecuteCloudAsync(
            () => _cloud!.SignInAsync(CloudEmail.Trim(), CloudPassword),
            "SIGN IN");

    public Task SignUpAsync() =>
        ExecuteCloudAsync(
            () => _cloud!.SignUpAsync(CloudEmail.Trim(), CloudPassword),
            "CREATE ACCOUNT");

    public Task SignOutAsync() =>
        ExecuteCloudAsync(() => _cloud!.SignOutAsync(), "SIGN OUT");

    public Task SyncNowAsync() =>
        ExecuteCloudAsync(() => _cloud!.SyncNowAsync(), "SYNC");

    public Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string confirmation)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) ||
            string.IsNullOrWhiteSpace(newPassword) ||
            string.IsNullOrWhiteSpace(confirmation))
        {
            StatusMessage = "ALL PASSWORD FIELDS ARE REQUIRED";
            return Task.CompletedTask;
        }

        if (newPassword != confirmation)
        {
            StatusMessage = "NEW PASSWORDS DO NOT MATCH";
            return Task.CompletedTask;
        }

        if (newPassword == currentPassword)
        {
            StatusMessage = "CHOOSE A DIFFERENT NEW PASSWORD";
            return Task.CompletedTask;
        }

        return ExecuteCloudAsync(
            () => _cloud!.ChangePasswordAsync(currentPassword, newPassword),
            "PASSWORD");
    }

    public Task ResolveConflictAsync(SyncConflict conflict, ConflictChoice choice) =>
        ExecuteCloudAsync(
            () => _cloud!.ResolveConflictAsync(conflict.Id, choice),
            "CONFLICT");

    public async Task AddChecklistAsync()
    {
        var name = NewChecklistName.Trim().ToUpperInvariant();
        if (name.Length == 0)
        {
            StatusMessage = "TYPE A LIST NAME FIRST";
            return;
        }

        if (Checklists.Any(checklist =>
                string.Equals(checklist.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "LIST NAME ALREADY EXISTS";
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var checklist = new TodoChecklist(
            Guid.NewGuid().ToString("N"),
            name,
            Checklists.Count(item => item.CanEdit),
            now,
            now);
        await ExecuteAsync(async () =>
        {
            await repository.UpsertChecklistAsync(checklist);
            NewChecklistName = string.Empty;
            await ReloadChecklistsAsync(checklist.Id);
            await ReloadAsync();
            StatusMessage = "LIST CREATED";
            _cloud?.RequestSync();
        });
    }

    public async Task RenameChecklistAsync()
    {
        if (SelectedChecklist is not { CanEdit: true } selected)
        {
            return;
        }

        var name = ChecklistEditorName.Trim().ToUpperInvariant();
        if (name.Length == 0)
        {
            StatusMessage = "LIST NAME CANNOT BE EMPTY";
            return;
        }

        var updated = selected.Model with
        {
            Name = name,
            UpdatedAtMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SyncState = SyncState.Dirty,
        };
        await ExecuteAsync(async () =>
        {
            await repository.UpsertChecklistAsync(updated);
            await ReloadChecklistsAsync(updated.Id);
            StatusMessage = "LIST RENAMED";
            _cloud?.RequestSync();
        });
    }

    public async Task DeleteChecklistAsync()
    {
        if (SelectedChecklist is not { CanEdit: true } selected)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await repository.DeleteChecklistAsync(
                selected.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await ReloadChecklistsAsync(PixelDoneChecklists.MainId);
            await ReloadAsync();
            StatusMessage = "LIST MOVED TO TRASH";
            _cloud?.RequestSync();
        });
    }

    public async Task AddTodoAsync()
    {
        if (SelectedChecklist is null || !IsTaskPage || IsTrashPage)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var due = new DateTimeOffset(DateTime.Now.AddDays(1).Date.AddHours(9))
            .ToUniversalTime()
            .ToUnixTimeMilliseconds();
        var item = TodoRules.Create(
            Guid.NewGuid().ToString("N"),
            NewTitle,
            NewPriority,
            due,
            now,
            checklistId: SelectedChecklist.Id,
            sortIndex: _allItems.Count);

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
            _cloud?.RequestSync();
        });
    }

    public async Task ToggleAsync(TodoItemViewModel todo)
    {
        var updated = todo.Model with
        {
            Completed = !todo.Model.Completed,
            UpdatedAtMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SyncState = SyncState.Dirty,
        };

        await ExecuteAsync(async () =>
        {
            await repository.UpsertAsync(updated);
            await ReloadAsync(updated.Id);
            StatusMessage = updated.Completed ? "TASK COMPLETED" : "TASK REOPENED";
            _cloud?.RequestSync();
        });
    }

    public async Task SaveSelectedAsync()
    {
        if (SelectedTodo is null || IsTrashPage)
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

        updated = updated with { SyncState = SyncState.Dirty };
        await ExecuteAsync(async () =>
        {
            await repository.UpsertAsync(updated);
            await ReloadAsync(updated.Id);
            StatusMessage = "CHANGES SAVED";
            _cloud?.RequestSync();
        });
    }

    public async Task AttachImageAsync(string sourcePath)
    {
        if (SelectedTodo is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var existing = await repository.GetAttachmentAsync(SelectedTodo.Id);
            var attachment = await _attachmentService.ImportAsync(
                sourcePath,
                SelectedTodo.Id,
                existing);
            await repository.UpsertAttachmentAsync(attachment);
            if (existing?.LocalPath != attachment.LocalPath)
            {
                WindowsAttachmentService.DeleteLocalFile(existing?.LocalPath);
            }

            await LoadAttachmentAsync(SelectedTodo.Id);
            StatusMessage = "IMAGE ATTACHED";
            _cloud?.RequestSync();
        });
    }

    public async Task RemoveImageAsync()
    {
        if (SelectedTodo is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var existing = await repository.GetAttachmentAsync(SelectedTodo.Id);
            await repository.RemoveAttachmentAsync(
                SelectedTodo.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            WindowsAttachmentService.DeleteLocalFile(existing?.LocalPath);
            await LoadAttachmentAsync(SelectedTodo.Id);
            StatusMessage = "IMAGE REMOVED";
            _cloud?.RequestSync();
        });
    }

    public async Task MoveSelectedAsync()
    {
        if (SelectedTodo is null || SelectedMoveTarget is null || IsTrashPage)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await repository.MoveToChecklistAsync(
                SelectedTodo.Id,
                SelectedMoveTarget.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await ReloadAsync();
            StatusMessage = $"TASK MOVED TO {SelectedMoveTarget.Name}";
            _cloud?.RequestSync();
        });
    }

    public string ExportMarkdown(MarkdownExportMode mode = MarkdownExportMode.Detailed) =>
        SelectedChecklist is { IsSettings: false } checklist
            ? TodoRules.ExportMarkdown(checklist.Model, _allItems, SortMode, mode)
            : string.Empty;

    public async Task InvokeDockActionAsync(DockItemViewModel item)
    {
        if (item.IsAdd)
        {
            return;
        }

        switch (item.Action)
        {
            case DockAction.Sort:
                SortMode = SortMode == TodoSortMode.Priority
                    ? TodoSortMode.Time
                    : TodoSortMode.Priority;
                break;
            case DockAction.Ddl:
                ShowDdl = !ShowDdl;
                break;
            case DockAction.HideDone:
                HideCompleted = !HideCompleted;
                break;
            case DockAction.CleanDone:
                await CleanCompletedAsync();
                break;
            case DockAction.QuickDelete:
                QuickDelete = !QuickDelete;
                break;
            case DockAction.BatchDelete:
                IsBatchDeleteMode = !IsBatchDeleteMode;
                break;
            case DockAction.ExportMarkdown:
            case null:
                break;
        }
    }

    public void SetBatchSelection(IEnumerable<TodoItemViewModel> selected)
    {
        _batchIds.Clear();
        foreach (var item in selected)
        {
            _batchIds.Add(item.Id);
        }

        OnPropertyChanged(nameof(BatchSelectionLabel));
    }

    public async Task DeleteBatchAsync()
    {
        if (!IsBatchDeleteMode || _batchIds.Count == 0 ||
            SelectedChecklist is not { IsSettings: false, IsTrash: false } checklist)
        {
            return;
        }

        var ids = _batchIds.ToArray();
        await ExecuteAsync(async () =>
        {
            var changed = await repository.MoveManyToTrashAsync(
                checklist.Id,
                ids,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            IsBatchDeleteMode = false;
            await ReloadAsync();
            StatusMessage = $"{changed} TASKS MOVED TO TRASH";
            _cloud?.RequestSync();
        });
    }

    public void ToggleDockAction(DockAction action)
    {
        _dockActions = DockRules.Toggle(_dockActions, action).ToList();
        PersistDockConfiguration();
    }

    public void MoveDockAction(DockAction action, int offset)
    {
        _dockActions = DockRules.Move(_dockActions, action, offset).ToList();
        PersistDockConfiguration();
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedTodo is null)
        {
            return;
        }

        var id = SelectedTodo.Id;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(async () =>
        {
            if (IsTrashPage)
            {
                await repository.DeletePermanentlyAsync(id, now);
                StatusMessage = "TASK DELETED FOREVER";
            }
            else
            {
                await repository.DeleteAsync(id, now);
                StatusMessage = "MOVED TO TRASH";
            }

            await ReloadAsync();
            _cloud?.RequestSync();
        });
    }

    public async Task QuickDeleteAsync(TodoItemViewModel todo)
    {
        if (IsTrashPage || IsSettingsPage)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await repository.DeleteAsync(
                todo.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await ReloadAsync();
            StatusMessage = "MOVED TO TRASH";
            _cloud?.RequestSync();
        });
    }

    public async Task RestoreSelectedAsync()
    {
        if (SelectedTodo is null || !IsTrashPage)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await repository.RestoreAsync(
                SelectedTodo.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await ReloadChecklistsAsync(PixelDoneChecklists.TrashId);
            await ReloadAsync();
            StatusMessage = "TASK RESTORED";
            _cloud?.RequestSync();
        });
    }

    public async Task CleanCompletedAsync()
    {
        if (SelectedChecklist is null || IsTrashPage || IsSettingsPage)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var changed = await repository.MoveCompletedToTrashAsync(
                SelectedChecklist.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await ReloadAsync();
            StatusMessage = $"{changed} COMPLETED TASKS MOVED";
            _cloud?.RequestSync();
        });
    }

    public async Task PurgeTrashAsync()
    {
        if (!IsTrashPage)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var changed = await repository.PurgeTrashAsync(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await ReloadAsync();
            StatusMessage = $"{changed} TASKS DELETED FOREVER";
            _cloud?.RequestSync();
        });
    }

    private async Task ReloadChecklistsAsync(string? selectedId = null)
    {
        selectedId ??= SelectedChecklist?.Id;
        var checklists = await repository.ListChecklistsAsync();
        Checklists.Clear();
        foreach (var checklist in checklists)
        {
            Checklists.Add(new ChecklistViewModel(checklist));
        }

        SelectedChecklist =
            Checklists.FirstOrDefault(item => item.Id == selectedId) ??
            Checklists.FirstOrDefault(item => item.Id == PixelDoneChecklists.MainId) ??
            Checklists.FirstOrDefault();
        RefreshMoveTargets();
    }

    private void RefreshMoveTargets()
    {
        var selectedId = SelectedChecklist?.Id;
        MoveTargets.Clear();
        foreach (var checklist in Checklists.Where(
                     value => value.CanEdit && value.Id != selectedId))
        {
            MoveTargets.Add(checklist);
        }

        SelectedMoveTarget = MoveTargets.FirstOrDefault();
    }

    private async Task ReloadAsync(string? selectedId = null)
    {
        selectedId ??= SelectedTodo?.Id;
        _allItems.Clear();
        if (SelectedChecklist is { IsSettings: false } selected)
        {
            _allItems.AddRange(await repository.ListAsync(selected.Id));
        }

        RefreshVisibleItems(selectedId);
        RaisePageState();
    }

    private async Task LoadAttachmentAsync(string? todoId)
    {
        if (todoId is null)
        {
            AttachmentPath = null;
            AttachmentPreview = null;
            return;
        }

        try
        {
            var attachment = await repository.GetAttachmentAsync(todoId);
            var path = attachment is { DeletedAtMillis: null } &&
                       !string.IsNullOrWhiteSpace(attachment.LocalPath) &&
                       File.Exists(attachment.LocalPath)
                ? attachment.LocalPath
                : null;
            AttachmentPath = path;
            AttachmentPreview = path is null
                ? null
                : new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch (Exception exception)
        {
            AttachmentPath = null;
            AttachmentPreview = null;
            StatusMessage = $"ATTACHMENT ERROR · {exception.Message.ToUpperInvariant()}";
        }
    }

    private void RefreshVisibleItems(string? selectedId = null)
    {
        selectedId ??= SelectedTodo?.Id;
        IEnumerable<TodoItem> visible = IsTrashPage
            ? _allItems
            : TodoRules.Visible(_allItems, SortMode, HideCompleted);
        Todos.Clear();
        foreach (var item in visible)
        {
            Todos.Add(new TodoItemViewModel(item, ShowDdl, QuickDelete && !IsTrashPage));
        }

        SelectedTodo = Todos.FirstOrDefault(todo => todo.Id == selectedId);
        OnPropertyChanged(nameof(OpenCountLabel));
        RefreshDock();
    }

    private void ApplySettings(PixelDoneSettings settings)
    {
        _dockActions = settings.EffectiveDockActions.ToList();
        HideCompleted = settings.HideCompleted;
        ShowDdl = settings.ShowDdl;
        QuickDelete = settings.QuickDelete;
        UpdatePrompts = settings.UpdatePrompts;
        EnhancedXHighAlarm = settings.EnhancedXHighAlarm;
        SortMode = settings.SortMode;
        Theme = settings.Theme;
        Language = settings.Language;
        DockPlusPlacement = settings.DockPlusPlacement;
        RefreshDock();
    }

    private void RefreshDock()
    {
        var actions = DockRules.Normalize(_dockActions)
            .Select(action => DockItemViewModel.ForAction(
                action,
                SortMode,
                ShowDdl,
                HideCompleted,
                QuickDelete,
                IsBatchDeleteMode))
            .ToList();
        var addIndex = DockPlusPlacement switch
        {
            DockPlusPlacement.LeftEdge => 0,
            DockPlusPlacement.RightEdge => actions.Count,
            _ => (actions.Count + 1) / 2,
        };
        actions.Insert(addIndex, DockItemViewModel.Add);

        DockItems.Clear();
        foreach (var item in actions)
        {
            DockItems.Add(item);
        }

        DockChoices.Clear();
        foreach (var action in DockRules.All)
        {
            var order = _dockActions.IndexOf(action);
            DockChoices.Add(new DockActionChoiceViewModel(
                action,
                action switch
                {
                    DockAction.Ddl => "DEADLINE",
                    DockAction.HideDone => "HIDE DONE",
                    DockAction.CleanDone => "CLEAN DONE",
                    DockAction.QuickDelete => "QUICK DELETE",
                    DockAction.BatchDelete => "BATCH DELETE",
                    DockAction.ExportMarkdown => "EXPORT MARKDOWN",
                    _ => action.ToString().ToUpperInvariant(),
                },
                order >= 0,
                order));
        }
    }

    private void PersistDockConfiguration()
    {
        RefreshDock();
        QueueSettingsSave();
    }

    private void QueueSettingsSave()
    {
        if (!_initialized)
        {
            return;
        }

        _settings = _settings with
        {
            Theme = Theme,
            Language = Language,
            SortMode = SortMode,
            ShowDdl = ShowDdl,
            HideCompleted = HideCompleted,
            QuickDelete = QuickDelete,
            UpdatePrompts = UpdatePrompts,
            EnhancedXHighAlarm = EnhancedXHighAlarm,
            DockActions = _dockActions,
            DockPlusPlacement = DockPlusPlacement,
        };
        _ = SaveSettingsBestEffortAsync(_settings);
    }

    private async Task SaveSettingsBestEffortAsync(PixelDoneSettings settings)
    {
        try
        {
            await repository.SaveSettingsAsync(settings);
            _cloud?.RequestSync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"SETTINGS ERROR · {exception.Message.ToUpperInvariant()}";
        }
    }

    private async void OnCloudStateChanged(object? sender, CloudState state)
    {
        IsSignedIn = state.IsSignedIn;
        CloudAccount = state.Account ?? string.Empty;
        CloudMessage = state.Message;
        if (state.IsSignedIn)
        {
            CloudPassword = string.Empty;
        }

        Conflicts.Clear();
        foreach (var conflict in state.Conflicts ?? [])
        {
            Conflicts.Add(conflict);
        }

        OnPropertyChanged(nameof(ConflictVisibility));
        StatusMessage = state.Message;
        if (state.Summary is not null)
        {
            try
            {
                await ReloadChecklistsAsync(SelectedChecklist?.Id);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                StatusMessage = $"SYNC RELOAD ERROR · {exception.Message.ToUpperInvariant()}";
            }
        }
    }

    private async Task ExecuteCloudAsync(Func<Task> action, string operation)
    {
        if (_cloud is null)
        {
            StatusMessage = "CLOUD IS NOT CONFIGURED";
            return;
        }

        if (operation is "SIGN IN" or "CREATE ACCOUNT" &&
            (string.IsNullOrWhiteSpace(CloudEmail) || string.IsNullOrWhiteSpace(CloudPassword)))
        {
            StatusMessage = "EMAIL AND PASSWORD ARE REQUIRED";
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = $"{operation} ERROR · {exception.Message.ToUpperInvariant()}";
            CloudMessage = StatusMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaisePageState()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(OpenCountLabel));
        OnPropertyChanged(nameof(IsTrashPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsTaskPage));
        OnPropertyChanged(nameof(CanEditChecklist));
        OnPropertyChanged(nameof(TaskPageVisibility));
        OnPropertyChanged(nameof(SettingsPageVisibility));
        OnPropertyChanged(nameof(TrashActionVisibility));
        OnPropertyChanged(nameof(NormalActionVisibility));
        OnPropertyChanged(nameof(DeleteActionLabel));
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
