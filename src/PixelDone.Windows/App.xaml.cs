using Microsoft.UI.Xaml;
using PixelDone.Core;
using PixelDone.Infrastructure;

namespace PixelDone.Windows;

public partial class App : Application
{
    private Window? _window;

    public ITodoRepository TodoRepository { get; }

    public App()
    {
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PixelDone");
        TodoRepository = new SqliteTodoRepository(Path.Combine(dataRoot, "pixeldone.db"));
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            WriteStartupFailure(exception);
            throw;
        }
    }

    private static void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        WriteStartupFailure(args.Exception);
    }

    private static void WriteStartupFailure(Exception exception)
    {
        var path = Environment.GetEnvironmentVariable("PIXELDONE_STARTUP_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never replace the original startup exception.
        }
    }
}
