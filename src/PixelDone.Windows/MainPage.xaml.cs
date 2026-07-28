using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PixelDone.Windows.ViewModels;

namespace PixelDone.Windows;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        var app = (App)Application.Current;
        ViewModel = new MainViewModel(app.TodoRepository);
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
        await ViewModel.DeleteSelectedAsync();
    }
}
