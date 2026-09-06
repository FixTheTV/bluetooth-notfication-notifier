using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Widget;
using BluetoothSMSNotifier.Bluetooth;
using BluetoothSMSNotifier.Notifications;
using BluetoothSMSNotifier.Services;

namespace BluetoothSMSNotifier
{
    [Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const int PermissionRequestCode = 42;
    private const string BluetoothConnectPermission = "android.permission.BLUETOOTH_CONNECT";
    private const string BluetoothScanPermission = "android.permission.BLUETOOTH_SCAN";
    private const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";

        private readonly List<BluetoothDeviceListItem> _pairedDevices = new();

        private Switch? _roleSwitch;
        private TextView? _roleDescription;
        private ListView? _deviceList;
        private Button? _startButton;
        private Button? _stopButton;
        private Button? _listenerSettingsButton;
        private TextView? _statusText;
        private ArrayAdapter<string>? _deviceAdapter;
        private string? _selectedDeviceAddress;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.activity_main);
            BindViews();
            WireEvents();
            RestoreUiState();
            LocalNotificationHelper.EnsureChannels(this);
            RequestMissingRuntimePermissions();
            RefreshPairedDevices();
        }

        protected override void OnResume()
        {
            base.OnResume();
            RefreshPairedDevices();
        }

        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == PermissionRequestCode)
            {
                RefreshPairedDevices();
                SetStatus(GetString(Resource.String.permissions_updated));
            }
        }

        private void BindViews()
        {
            _roleSwitch = FindViewById<Switch>(Resource.Id.roleSwitch);
            _roleDescription = FindViewById<TextView>(Resource.Id.roleDescription);
            _deviceList = FindViewById<ListView>(Resource.Id.deviceList);
            _startButton = FindViewById<Button>(Resource.Id.startButton);
            _stopButton = FindViewById<Button>(Resource.Id.stopButton);
            _listenerSettingsButton = FindViewById<Button>(Resource.Id.listenerSettingsButton);
            _statusText = FindViewById<TextView>(Resource.Id.statusText);
        }

        private void WireEvents()
        {
            _roleSwitch!.CheckedChange += (_, args) =>
            {
                AppPreferences.SetRole(this, args.IsChecked ? AppRole.Receiver : AppRole.Sender);
                UpdateRoleUi();
            };

            _deviceList!.ItemClick += (_, args) =>
            {
                if (args.Position < 0 || args.Position >= _pairedDevices.Count)
                {
                    return;
                }

                BluetoothDeviceListItem selectedDevice = _pairedDevices[args.Position];
                _selectedDeviceAddress = selectedDevice.Address;
                AppPreferences.SetReceiverDeviceAddress(this, selectedDevice.Address);
                SetStatus(string.Format(GetString(Resource.String.selected_device_status), selectedDevice.DisplayName));
            };

            _startButton!.Click += async (_, _) => await StartSelectedModeAsync();
            _stopButton!.Click += (_, _) => StopServices();
            _listenerSettingsButton!.Click += (_, _) =>
            {
                StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
            };
        }

        private void RestoreUiState()
        {
            _roleSwitch!.Checked = AppPreferences.GetRole(this) == AppRole.Receiver;
            _selectedDeviceAddress = AppPreferences.GetReceiverDeviceAddress(this);
            UpdateRoleUi();
        }

        private async Task StartSelectedModeAsync()
        {
            RequestMissingRuntimePermissions();

            if (AppPreferences.GetRole(this) == AppRole.Sender)
            {
                AppPreferences.SetRole(this, AppRole.Sender);
                await BluetoothServerManager.Instance.StartAsync(this);
                SetStatus(GetString(Resource.String.sender_started_status));
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedDeviceAddress))
            {
                SetStatus(GetString(Resource.String.pick_device_first_status));
                return;
            }

            AppPreferences.SetRole(this, AppRole.Receiver);
            StartReceiverService(_selectedDeviceAddress);
            SetStatus(GetString(Resource.String.receiver_started_status));
        }

        private void StartReceiverService(string deviceAddress)
        {
            Intent intent = new(this, typeof(BluetoothReceiverService));
            intent.SetAction(BluetoothProtocol.ReceiverActionStart);
            intent.PutExtra(BluetoothProtocol.ExtraDeviceAddress, deviceAddress);

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                StartForegroundService(intent);
            }
            else
            {
                StartService(intent);
            }
        }

        private void StopServices()
        {
            BluetoothServerManager.Instance.Stop();

            Intent intent = new(this, typeof(BluetoothReceiverService));
            intent.SetAction(BluetoothProtocol.ReceiverActionStop);
            StopService(intent);

            SetStatus(GetString(Resource.String.stopped_status));
        }

        private void UpdateRoleUi()
        {
            bool receiverMode = AppPreferences.GetRole(this) == AppRole.Receiver;
            _roleSwitch!.Text = receiverMode
                ? GetString(Resource.String.receiver_mode)
                : GetString(Resource.String.sender_mode);
            _roleDescription!.Text = receiverMode
                ? GetString(Resource.String.receiver_mode_description)
                : GetString(Resource.String.sender_mode_description);
            _startButton!.Text = receiverMode
                ? GetString(Resource.String.connect_button)
                : GetString(Resource.String.start_sender_button);
            _deviceList!.Enabled = receiverMode;
        }

        private void RefreshPairedDevices()
        {
            _pairedDevices.Clear();

            if (!HasBluetoothConnectPermission())
            {
                UpdateDeviceList();
                SetStatus(GetString(Resource.String.bluetooth_permission_needed_status));
                return;
            }

            BluetoothAdapter? adapter = BluetoothAdapterProvider.GetAdapter(this);
            if (adapter is null)
            {
                UpdateDeviceList();
                SetStatus(GetString(Resource.String.bluetooth_unavailable_status));
                return;
            }

            foreach (BluetoothDevice device in adapter.BondedDevices ?? Enumerable.Empty<BluetoothDevice>())
            {
                string address = device.Address ?? string.Empty;
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                _pairedDevices.Add(new BluetoothDeviceListItem(
                    device.Name ?? GetString(Resource.String.unknown_device),
                    address));
            }

            UpdateDeviceList();
        }

        private void UpdateDeviceList()
        {
            string[] labels = _pairedDevices.Count == 0
                ? new[] { GetString(Resource.String.no_paired_devices) }
                : _pairedDevices.Select(device => device.DisplayName).ToArray();

            _deviceAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItemSingleChoice, labels);
            _deviceList!.Adapter = _deviceAdapter;
            _deviceList.ChoiceMode = ChoiceMode.Single;

            int selectedIndex = _pairedDevices.FindIndex(device => device.Address == _selectedDeviceAddress);
            if (selectedIndex >= 0)
            {
                _deviceList.SetItemChecked(selectedIndex, true);
            }
        }

        private void RequestMissingRuntimePermissions()
        {
            string[] missingPermissions = GetRequiredPermissions()
                .Where(permission => CheckSelfPermission(permission) != Permission.Granted)
                .ToArray();

            if (missingPermissions.Length > 0)
            {
                RequestPermissions(missingPermissions, PermissionRequestCode);
            }
        }

        private IEnumerable<string> GetRequiredPermissions()
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                yield return BluetoothConnectPermission;
                yield return BluetoothScanPermission;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                yield return PostNotificationsPermission;
            }
        }

        private bool HasBluetoothConnectPermission()
        {
            return !OperatingSystem.IsAndroidVersionAtLeast(31) ||
                CheckSelfPermission(BluetoothConnectPermission) == Permission.Granted;
        }

        private void SetStatus(string message)
        {
            _statusText!.Text = message;
        }

        private sealed record BluetoothDeviceListItem(string Name, string Address)
        {
            public string DisplayName => $"{Name} ({Address})";
        }
    }
}
