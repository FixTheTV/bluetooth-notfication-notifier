using System.Text;
using Android.Bluetooth;
using Android.Content;
using Android.Util;

namespace BluetoothSMSNotifier.Bluetooth;

public sealed class BluetoothServerManager : IDisposable
{
    private const string LogTag = "BtServerManager";
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();

    private BluetoothServerSocket? _serverSocket;
    private BluetoothSocket? _clientSocket;
    private StreamWriter? _writer;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private bool _disposed;

    public static BluetoothServerManager Instance { get; } = new();

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _listenTask is { IsCompleted: false };
            }
        }
    }

    public Task StartAsync(Context context, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_listenTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Context appContext = context.ApplicationContext ?? context;
            CancellationToken listenToken = _listenCts.Token;
            _listenTask = Task.Run(() => ListenLoop(appContext, listenToken), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task SendAsync(string jsonPayload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return;
        }

        StreamWriter? writer;
        lock (_sync)
        {
            writer = _writer;
        }

        if (writer is null)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(jsonPayload).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Warn(LogTag, $"Write failed, dropping active Bluetooth client: {ex.Message}");
            CloseClient();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        BluetoothServerSocket? serverSocket;

        lock (_sync)
        {
            cts = _listenCts;
            serverSocket = _serverSocket;
            _listenCts = null;
            _serverSocket = null;
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
            serverSocket?.Close();
        }
        catch (IOException)
        {
        }

        CloseClient();
        cts?.Dispose();
    }

    private void ListenLoop(Context context, CancellationToken cancellationToken)
    {
        BluetoothAdapter? adapter = BluetoothAdapterProvider.GetAdapter(context);
        if (adapter is null)
        {
            Log.Warn(LogTag, "Bluetooth adapter is not available.");
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            BluetoothServerSocket? serverSocket = null;

            try
            {
                serverSocket = adapter.ListenUsingRfcommWithServiceRecord(
                    BluetoothProtocol.ServiceName,
                    BluetoothProtocol.ServiceUuid) ?? throw new IOException("Unable to open Bluetooth server socket.");

                lock (_sync)
                {
                    _serverSocket = serverSocket;
                }

                BluetoothSocket socket = serverSocket.Accept() ?? throw new IOException("Accepted Bluetooth socket was null.");
                cancellationToken.ThrowIfCancellationRequested();
                ReplaceClient(socket);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or Java.IO.IOException or ObjectDisposedException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log.Warn(LogTag, $"Accept failed: {ex.Message}");
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }
            }
            finally
            {
                try
                {
                    serverSocket?.Close();
                }
                catch (IOException)
                {
                }

                lock (_sync)
                {
                    if (ReferenceEquals(_serverSocket, serverSocket))
                    {
                        _serverSocket = null;
                    }
                }
            }
        }
    }

    private void ReplaceClient(BluetoothSocket socket)
    {
        CloseClient();

        StreamWriter writer = new(socket.OutputStream!, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        lock (_sync)
        {
            _clientSocket = socket;
            _writer = writer;
        }

        Log.Info(LogTag, $"Bluetooth client connected: {socket.RemoteDevice?.Address ?? "unknown"}");
    }

    private void CloseClient()
    {
        StreamWriter? writer;
        BluetoothSocket? socket;

        lock (_sync)
        {
            writer = _writer;
            socket = _clientSocket;
            _writer = null;
            _clientSocket = null;
        }

        try
        {
            writer?.Dispose();
        }
        catch (IOException)
        {
        }

        try
        {
            socket?.Close();
        }
        catch (IOException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BluetoothServerManager));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _writeGate.Dispose();
    }
}
