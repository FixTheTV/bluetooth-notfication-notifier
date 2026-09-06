using Android.App;
using Android.Content;
using Android.OS;
using Android.Service.Notification;
using Android.Util;
using BluetoothSMSNotifier.Bluetooth;
using BluetoothSMSNotifier.Models;

namespace BluetoothSMSNotifier.Services;

[Service(
    Name = "com.companyname.bluetoothsmsnotifier.MyNotificationListener",
    Label = "Bluetooth Notification Sync",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public sealed class MyNotificationListener : NotificationListenerService
{
    private const string LogTag = "BtNotificationListener";

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();

        if (AppPreferences.GetRole(this) == AppRole.Sender)
        {
            _ = BluetoothServerManager.Instance.StartAsync(this);
        }
    }

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        base.OnNotificationPosted(sbn);

        if (sbn is null || !ShouldForward(sbn))
        {
            return;
        }

        NotificationPayload? payload = CreatePayload(sbn);
        if (payload is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await BluetoothServerManager.Instance.SendAsync(payload.ToJson()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, $"Unable to forward notification: {ex.Message}");
            }
        });
    }

    private bool ShouldForward(StatusBarNotification sbn)
    {
        if (AppPreferences.GetRole(this) != AppRole.Sender)
        {
            return false;
        }

        string packageName = sbn.PackageName ?? string.Empty;
        if (packageName.Equals(PackageName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (packageName.Equals("android", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("com.android.systemui", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Notification? notification = sbn.Notification;
        if (notification is null)
        {
            return false;
        }

        if ((notification.Flags & NotificationFlags.GroupSummary) == NotificationFlags.GroupSummary)
        {
            return false;
        }

        return true;
    }

    private NotificationPayload? CreatePayload(StatusBarNotification sbn)
    {
        Notification? notification = sbn.Notification;
        if (notification is null)
        {
            return null;
        }

        Bundle? extras = notification.Extras;
        string title = extras?.GetCharSequence(Notification.ExtraTitle)?.ToString()?.Trim() ?? string.Empty;
        string text = extras?.GetCharSequence(Notification.ExtraText)?.ToString()?.Trim() ?? string.Empty;
        string bigText = extras?.GetCharSequence(Notification.ExtraBigText)?.ToString()?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(bigText))
        {
            text = bigText;
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new NotificationPayload
        {
            AppName = ResolveAppName(sbn.PackageName),
            Title = title,
            Text = text,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(sbn.PostTime).ToUniversalTime()
        };
    }

    private string ResolveAppName(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return "Unknown app";
        }

        try
        {
            Android.Content.PM.ApplicationInfo appInfo = PackageManager!.GetApplicationInfo(packageName, 0);
            return PackageManager.GetApplicationLabel(appInfo)?.ToString() ?? packageName;
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return packageName;
        }
    }
}
