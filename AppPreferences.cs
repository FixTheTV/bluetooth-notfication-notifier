using Android.Content;

namespace BluetoothSMSNotifier;

public enum AppRole
{
    Sender,
    Receiver
}

public static class AppPreferences
{
    private const string PreferencesName = "bluetooth_notification_sync";
    private const string RoleKey = "role";
    private const string ReceiverDeviceAddressKey = "receiver_device_address";

    public static AppRole GetRole(Context context)
    {
        string value = GetPreferences(context).GetString(RoleKey, AppRole.Sender.ToString()) ?? AppRole.Sender.ToString();
        return Enum.TryParse(value, ignoreCase: true, out AppRole role) ? role : AppRole.Sender;
    }

    public static void SetRole(Context context, AppRole role)
    {
        GetPreferences(context).Edit()?.PutString(RoleKey, role.ToString())?.Apply();
    }

    public static string? GetReceiverDeviceAddress(Context context) =>
        GetPreferences(context).GetString(ReceiverDeviceAddressKey, null);

    public static void SetReceiverDeviceAddress(Context context, string deviceAddress)
    {
        GetPreferences(context).Edit()?.PutString(ReceiverDeviceAddressKey, deviceAddress)?.Apply();
    }

    private static ISharedPreferences GetPreferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
