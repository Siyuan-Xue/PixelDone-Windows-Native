using Microsoft.UI.Xaml;
using PixelDone.Core;
using PixelDone.Infrastructure;
using PixelDone.Windows.Services;

namespace PixelDone.Windows;

public partial class App : Application
{
    private Window? _window;

    public ITodoRepository TodoRepository { get; }
    public CloudSessionService? CloudService { get; }
    public WindowsAttachmentService AttachmentService { get; }
    public AppUpdateService UpdateService { get; } = new();
    public WindowsNotificationService NotificationService { get; } = new();
    public Window? MainAppWindow => _window;
    public string CloudConfigurationMessage { get; } =
        "CLOUD READY · SIGN IN OR CREATE AN ACCOUNT";

    public App()
    {
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "com.milesxue.pixeldone.windows",
            "data");
        var repository = new SqliteTodoRepository(
            Path.Combine(dataRoot, "pixeldone.sqlite3"));
        TodoRepository = repository;
        AttachmentService = new WindowsAttachmentService(
            Path.Combine(dataRoot, "attachments", "local"));
        try
        {
            var client = SupabaseClient.FromEnvironment();
            CloudService = new CloudSessionService(
                client,
                new SyncEngine(
                    client,
                    repository,
                    repository,
                    Path.Combine(dataRoot, "attachments", "cloud-cache")),
                new WindowsCredentialStore());
        }
        catch (Exception exception)
        {
            CloudConfigurationMessage = $"CLOUD NOT CONFIGURED · {exception.Message}";
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var commandLineArguments = Environment.GetCommandLineArgs().Skip(1);
            var activationArguments = args.Arguments
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (commandLineArguments
                .Concat(activationArguments)
                .Contains("--notify-due", StringComparer.Ordinal))
            {
                _ = DeliverRemindersAndExitAsync();
                return;
            }

            NotificationService.Register();
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            WriteStartupFailure(exception);
            throw;
        }
    }

    private async Task DeliverRemindersAndExitAsync()
    {
        var exitCode = 0;
        try
        {
            WriteStartupTrace("REMINDER WORKER START");
            await TodoRepository.InitializeAsync();
            WriteStartupTrace("REMINDER REPOSITORY READY");
            await NotificationService.DeliverDueAsync(TodoRepository);
            WriteStartupTrace("REMINDER DELIVERY COMPLETE");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            WriteStartupFailure(exception);
        }
        finally
        {
            WriteStartupTrace("REMINDER WORKER STOPPING");
            NotificationService.Dispose();
            WriteStartupTrace("REMINDER WORKER EXIT");
            Environment.Exit(exitCode);
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
        WriteStartupTrace(exception.ToString());
    }

    private static void WriteStartupTrace(string message)
    {
        var path = Environment.GetEnvironmentVariable("PIXELDONE_STARTUP_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never replace the original startup exception.
        }
    }
}
