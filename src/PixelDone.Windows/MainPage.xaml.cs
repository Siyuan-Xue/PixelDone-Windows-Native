using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PixelDone.Windows.ViewModels;
using PixelDone.Core;
using PixelDone.Infrastructure;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;

namespace PixelDone.Windows;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        var app = (App)Application.Current;
        ViewModel = new MainViewModel(
            app.TodoRepository,
            app.CloudService,
            app.AttachmentService,
            app.UpdateService,
            app.CloudConfigurationMessage);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    private async void AddTodo_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddTodoAsync();
        NewTodoTextBox.Focus(FocusState.Programmatic);
    }

    private async void NewTodoTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.AddTodoAsync();
        }
    }

    private async void ToggleTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoItemViewModel todo })
        {
            await ViewModel.ToggleAsync(todo);
        }
    }

    private async void SaveTodo_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveSelectedAsync();
    }

    private async void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmAsync(
            ViewModel.IsTrashPage ? "DELETE FOREVER?" : "MOVE TASK TO TRASH?",
            ViewModel.IsTrashPage
                ? "This task and its local attachment will be permanently deleted."
                : "You can restore the task from Trash.",
            ViewModel.IsTrashPage ? "DELETE FOREVER" : "MOVE TO TRASH");
        if (confirmed)
        {
            await ViewModel.DeleteSelectedAsync();
        }
    }

    private async void QuickDeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoItemViewModel todo })
        {
            await ViewModel.QuickDeleteAsync(todo);
        }
    }

    private async void AddChecklist_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddChecklistAsync();
    }

    private async void RenameChecklist_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RenameChecklistAsync();
    }

    private async void DeleteChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync(
            "DELETE CHECKLIST?",
            "Its tasks will be moved to Trash and remain recoverable.",
            "DELETE"))
        {
            await ViewModel.DeleteChecklistAsync();
        }
    }

    private async void CleanCompleted_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync(
            "CLEAN COMPLETED TASKS?",
            "Every completed task in this checklist will move to Trash.",
            "CLEAN DONE"))
        {
            await ViewModel.CleanCompletedAsync();
        }
    }

    private async void RestoreTodo_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RestoreSelectedAsync();
    }

    private async void PurgeTrash_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync(
            "EMPTY TRASH?",
            "All tasks in Trash will be permanently deleted.",
            "DELETE ALL"))
        {
            await ViewModel.PurgeTrashAsync();
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloudPassword = CloudPasswordBox.Password;
        await ViewModel.SignInAsync();
        CloudPasswordBox.Password = string.Empty;
    }

    private async void SignUp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloudPassword = CloudPasswordBox.Password;
        await ViewModel.SignUpAsync();
        CloudPasswordBox.Password = string.Empty;
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SignOutAsync();
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncNowAsync();
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ChangePasswordAsync(
            CurrentPasswordBox.Password,
            NewPasswordBox.Password,
            ConfirmPasswordBox.Password);
        CurrentPasswordBox.Password = string.Empty;
        NewPasswordBox.Password = string.Empty;
        ConfirmPasswordBox.Password = string.Empty;
    }

    private async void KeepLocal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SyncConflict conflict })
        {
            await ViewModel.ResolveConflictAsync(conflict, ConflictChoice.KeepLocal);
        }
    }

    private async void KeepCloud_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SyncConflict conflict })
        {
            await ViewModel.ResolveConflictAsync(conflict, ConflictChoice.KeepCloud);
        }
    }

    private async void AttachImage_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        if (app.MainAppWindow is null)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(app.MainAppWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.AttachImageAsync(file.Path);
        }
    }

    private async void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RemoveImageAsync();
    }

    private async void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.AttachmentPath))
        {
            var file = await StorageFile.GetFileFromPathAsync(ViewModel.AttachmentPath);
            _ = await Launcher.LaunchFileAsync(file);
        }
    }

    private async void MoveTodo_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.MoveSelectedAsync();
    }

    private async void CopyMarkdown_Click(object sender, RoutedEventArgs e)
    {
        await ChooseMarkdownExportAsync();
    }

    private async void DockAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DockItemViewModel item })
        {
            return;
        }

        if (item.IsAdd)
        {
            NewTodoTextBox.Focus(FocusState.Programmatic);
            return;
        }

        if (item.Action == DockAction.ExportMarkdown)
        {
            await ChooseMarkdownExportAsync();
            return;
        }
        if (item.Action == DockAction.CleanDone)
        {
            if (await ConfirmAsync(
                "CLEAN COMPLETED TASKS?",
                "Every completed task in this checklist will move to Trash.",
                "CLEAN DONE"))
            {
                await ViewModel.CleanCompletedAsync();
            }
            return;
        }

        await ViewModel.InvokeDockActionAsync(item);
        if (!ViewModel.IsBatchDeleteMode)
        {
            TodoList.SelectedItems.Clear();
        }
    }

    private void TodoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsBatchDeleteMode)
        {
            ViewModel.SetBatchSelection(TodoList.SelectedItems.OfType<TodoItemViewModel>());
        }
    }

    private void CancelBatchDelete_Click(object sender, RoutedEventArgs e)
    {
        TodoList.SelectedItems.Clear();
        ViewModel.IsBatchDeleteMode = false;
    }

    private async void DeleteBatch_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync(
            "MOVE SELECTED TASKS?",
            "The selected tasks will move to Trash in one transaction.",
            "MOVE TO TRASH"))
        {
            await ViewModel.DeleteBatchAsync();
            TodoList.SelectedItems.Clear();
        }
    }

    private void ToggleDockAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DockActionChoiceViewModel choice })
        {
            ViewModel.ToggleDockAction(choice.Action);
        }
    }

    private void MoveDockActionUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DockActionChoiceViewModel choice })
        {
            ViewModel.MoveDockAction(choice.Action, -1);
        }
    }

    private void MoveDockActionDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DockActionChoiceViewModel choice })
        {
            ViewModel.MoveDockAction(choice.Action, 1);
        }
    }

    private async Task ChooseMarkdownExportAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "EXPORT MARKDOWN",
            Content = "Choose a compact task list or include priority, deadline, and repeat details.",
            PrimaryButtonText = "COPY DETAILED",
            SecondaryButtonText = "COPY SIMPLE",
            CloseButtonText = "CANCEL",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            CopyMarkdownToClipboard(MarkdownExportMode.Detailed);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            CopyMarkdownToClipboard(MarkdownExportMode.Simple);
        }
    }

    private void CopyMarkdownToClipboard(MarkdownExportMode mode)
    {
        var markdown = ViewModel.ExportMarkdown(mode);
        if (markdown.Length == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(markdown);
        Clipboard.SetContent(package);
        ViewModel.StatusMessage = "MARKDOWN COPIED";
    }

    private async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "CANCEL",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CheckForUpdatesAsync();
    }

    private async void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.UpdateReleasePage is { } releasePage)
        {
            _ = await Launcher.LaunchUriAsync(releasePage);
        }
    }

    private void NavigateBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateBack();
    }
}
