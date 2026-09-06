using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Android.Util;
using AndroidX.Core.App;
using BluetoothSMSNotifier.Bluetooth;
using BluetoothSMSNotifier.Notifications;

namespace BluetoothSMSNotifier.Services;

[Service(
    Name = "com.companyname.bluetoothsmsnotifier.BluetoothReceiverService",
    Exported = false,
    ForegroundServiceType = Android.Content.PM.ForegroundService.TypeConnectedDevice)]
public sealed class BluetoothReceiverService : Service
{
    private const string LogTag = "BtReceiverService";
    private const int ForegroundNotificationId = 1001;

    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private BluetoothSocket? _socket;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == BluetoothProtocol.ReceiverActionStop)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        string? deviceAddress = intent?.GetStringExtra(BluetoothProtocol.ExtraDeviceAddress)
            ?? AppPreferences.GetReceiverDeviceAddress(this);

        if (string.IsNullOrWhiteSpace(deviceAddress))
        {
            Log.Warn(LogTag, "Receiver service started without a Bluetooth device address.");
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        AppPreferences.SetReceiverDeviceAddress(this, deviceAddress);
        LocalNotificationHelper.EnsureChannels(this);
        StartForeground(ForegroundNotificationId, BuildForegroundNotification("Waiting for sender phone"));
        StartWorker(deviceAddress);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopWorker();
        base.OnDestroy();
    }

    private void StartWorker(string deviceAddress)
    {
        lock (_sync)
        {
            if (_workerTask is { IsCompleted: false })
            {
                return;
            }

            CancellationTokenSource cts = new();
            _cts = cts;
            _workerTask = Task.Run(() => ConnectLoopAsync(deviceAddress, cts.Token));
        }
    }

    private void StopWorker()
    {
        CancellationTokenSource? cts;
        BluetoothSocket? socket;

        lock (_sync)
        {
            cts = _cts;
            socket = _socket;
            _cts = null;
            _socket = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            socket?.Close();
        }
        catch (IOException)
        {
        }

        cts?.Dispose();
    }

    private async Task ConnectLoopAsync(string deviceAddress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            BluetoothSocket? socket = null;

            try
            {
                BluetoothAdapter? adapter = BluetoothAdapterProvider.GetAdapter(this);
                if (adapter is null)
                {
                    Log.Warn(LogTag, "Bluetooth adapter is not available.");
                    await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                BluetoothDevice device = adapter.GetRemoteDevice(deviceAddress)
                    ?? throw new IOException("Unable to resolve paired Bluetooth device.");
                adapter.CancelDiscovery();

                string displayName = device.Name ?? device.Address ?? deviceAddress;
                UpdateForegroundNotification($"Connecting to {displayName}");

                socket = device.CreateRfcommSocketToServiceRecord(BluetoothProtocol.ServiceUuid)
                    ?? throw new IOException("Unable to create Bluetooth RFCOMM socket.");
                lock (_sync)
                {
                    _socket = socket;
                }

                await Task.Run(() => socket.Connect(), cancellationToken).ConfigureAwait(false);
                UpdateForegroundNotification($"Connected to {displayName}");
                await ReadLoopAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or Java.IO.IOException or ObjectDisposedException or ArgumentException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log.Warn(LogTag, $"Bluetooth connection lost: {ex.Message}");
                    UpdateForegroundNotification("Connection lost. Reconnecting...");
                    await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_socket, socket))
                    {
                        _socket = null;
                    }
                }

                try
                {
                    socket?.Close();
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private async Task ReadLoopAsync(BluetoothSocket socket, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(socket.InputStream!);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new IOException("Bluetooth stream closed.");
            }

            await LocalNotificationHelper.ShowSyncedNotificationAsync(this, line, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Task DelayBeforeReconnect(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

    private Notification BuildForegroundNotification(string status)
    {
        NotificationCompat.Builder builder = new(this, LocalNotificationHelper.ServiceChannelId);
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetContentTitle(GetString(Resource.String.receiver_service_title));
        builder.SetContentText(status);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetPriority(NotificationCompat.PriorityLow);

        return builder.Build() ?? throw new InvalidOperationException("Unable to build foreground notification.");
    }

    private void UpdateForegroundNotification(string status)
    {
        NotificationManagerCompat.From(this)?.Notify(ForegroundNotificationId, BuildForegroundNotification(status));
    }
}
