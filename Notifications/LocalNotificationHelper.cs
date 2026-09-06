using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using BluetoothSMSNotifier.Models;
using System.Runtime.Versioning;

namespace BluetoothSMSNotifier.Notifications;

public static class LocalNotificationHelper
{
    public const string SyncedChannelId = "synced_notifications";
    public const string ServiceChannelId = "bluetooth_receiver_service";

    public static void EnsureChannels(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        CreateChannels(context);
    }

    [SupportedOSPlatform("android26.0")]
    private static void CreateChannels(Context context)
    {
        NotificationManager? manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null)
        {
            return;
        }

        NotificationChannel syncedChannel = new(
            SyncedChannelId,
            "Synced Notifications",
            NotificationImportance.Default)
        {
            Description = "Notifications received from the paired sender phone."
        };

        NotificationChannel serviceChannel = new(
            ServiceChannelId,
            "Bluetooth Sync Service",
            NotificationImportance.Low)
        {
            Description = "Connection status for Bluetooth notification sync."
        };

        manager.CreateNotificationChannel(syncedChannel);
        manager.CreateNotificationChannel(serviceChannel);
    }

    public static Task ShowSyncedNotificationAsync(
        Context context,
        string jsonPayload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NotificationPayload? payload = NotificationPayload.FromJson(jsonPayload);
        if (payload is null || !CanPostNotifications(context))
        {
            return Task.CompletedTask;
        }

        EnsureChannels(context);

        string title = string.IsNullOrWhiteSpace(payload.Title)
            ? payload.AppName
            : $"{payload.AppName}: {payload.Title}";
        string text = payload.Text;

        NotificationCompat.Builder builder = new(context, SyncedChannelId);
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetContentTitle(title);
        builder.SetContentText(text);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(text));
        builder.SetWhen(payload.Timestamp.ToUnixTimeMilliseconds());
        builder.SetShowWhen(true);
        builder.SetAutoCancel(true);
        builder.SetPriority(NotificationCompat.PriorityDefault);

        Notification? notification = builder.Build();
        if (notification is null)
        {
            return Task.CompletedTask;
        }

        int notificationId = HashCode.Combine(payload.AppName, payload.Title, payload.Text, payload.Timestamp);
        NotificationManagerCompat.From(context)?.Notify(notificationId, notification);
        return Task.CompletedTask;
    }

    private static bool CanPostNotifications(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        return context.CheckSelfPermission("android.permission.POST_NOTIFICATIONS")
            == Android.Content.PM.Permission.Granted;
    }
}
