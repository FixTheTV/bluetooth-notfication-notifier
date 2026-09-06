namespace BluetoothSMSNotifier.Bluetooth;

public static class BluetoothProtocol
{
    public const string ServiceName = "Bluetooth Notification Sync";
    public const string SerialPortProfileUuid = "00001101-0000-1000-8000-00805F9B34FB";
    public const string ReceiverActionStart = "com.companyname.bluetoothsmsnotifier.action.START_RECEIVER";
    public const string ReceiverActionStop = "com.companyname.bluetoothsmsnotifier.action.STOP_RECEIVER";
    public const string ExtraDeviceAddress = "com.companyname.bluetoothsmsnotifier.extra.DEVICE_ADDRESS";

    public static Java.Util.UUID ServiceUuid { get; } = Java.Util.UUID.FromString(SerialPortProfileUuid)!;
}
