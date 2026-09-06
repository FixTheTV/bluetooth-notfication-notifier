using Android.Bluetooth;
using Android.Content;

namespace BluetoothSMSNotifier.Bluetooth;

public static class BluetoothAdapterProvider
{
    public static BluetoothAdapter? GetAdapter(Context context)
    {
        BluetoothManager? manager = (BluetoothManager?)context.GetSystemService(Context.BluetoothService);
        return manager?.Adapter;
    }
}
