using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PixelDone.Core;

namespace PixelDone.Windows.Services;

public sealed class WindowsNotificationService : IDisposable
{
    private bool _registered;

    public bool Register()
    {
        if (_registered)
        {
            return true;
        }

        if (!AppNotificationManager.IsSupported())
        {
            return false;
        }

        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    public async Task DeliverDueAsync(
        ITodoRepository repository,
        CancellationToken cancellationToken = default)
    {
        if (!Register())
        {
            return;
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var settings = await repository.GetSettingsAsync(cancellationToken);
        foreach (var reminder in await repository.ListDueRemindersAsync(now, cancellationToken))
        {
            var builder = new AppNotificationBuilder()
                .AddText(reminder.Item.Title)
                .AddText(
                    $"{reminder.Item.Priority.ToString().ToUpperInvariant()} · " +
                    reminder.Item.ReminderRepeat.ToString().ToUpperInvariant());
            if (reminder.Item.Priority == TodoPriority.XHigh &&
                settings.EnhancedXHighAlarm)
            {
                builder.SetScenario(AppNotificationScenario.Urgent);
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
            await repository.MarkReminderDeliveredAsync(
                reminder.Item.Id,
                reminder.OccurrenceAtMillis,
                now,
                cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
            _registered = false;
        }
    }
}
