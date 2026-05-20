using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace PimaxVrcSupervisor.BaseStations;

internal enum BaseStationVersion
{
    Unknown,
    V1,
    V2
}

internal enum BaseStationPowerDownMode
{
    Sleep,
    Standby
}

internal enum BaseStationPowerState
{
    Unknown,
    Unsupported,
    Sleeping,
    Standby,
    Awake,
    Waking
}

internal static class BaseStationCommandTiming
{
    public static readonly TimeSpan InterStationDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PowerDownStateReadDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PowerOnRetryPassDelay = TimeSpan.FromSeconds(30);
    public const int PowerOnPasses = 3;
    public const int PowerOnAttempts = 2;
    public const int PowerDownFallbackPasses = 2;
}

internal sealed class BaseStationDevice
{
    public string FriendlyName { get; set; } = "";
    public string Name { get; set; } = "";
    public string BluetoothAddress { get; set; } = "";
    public BaseStationVersion Version { get; set; }
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "";
    public bool PowerStateReadUnsupported { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Name : FriendlyName;
    public bool RequiresId => EffectiveVersion == BaseStationVersion.V1;
    public bool SupportsStandby => EffectiveVersion == BaseStationVersion.V2;
    public bool RequiresExtendedPowerOnPasses => EffectiveVersion == BaseStationVersion.V1 || PowerStateReadUnsupported;
    public BaseStationVersion EffectiveVersion => Version == BaseStationVersion.Unknown ? InferVersion(Name) : Version;
    public ulong BluetoothAddressValue => BluetoothAddressConverter.StringToAddress(BluetoothAddress);

    public BaseStationDevice WithDefaults()
    {
        if (Version == BaseStationVersion.Unknown)
        {
            Version = InferVersion(Name);
        }

        if (string.IsNullOrWhiteSpace(FriendlyName))
        {
            FriendlyName = Name;
        }

        return this;
    }

    public static BaseStationVersion InferVersion(string name)
    {
        if (name.StartsWith("HTC BS", StringComparison.OrdinalIgnoreCase))
        {
            return BaseStationVersion.V1;
        }

        if (name.StartsWith("LHB-", StringComparison.OrdinalIgnoreCase))
        {
            return BaseStationVersion.V2;
        }

        return BaseStationVersion.Unknown;
    }
}

internal sealed class BaseStationPowerDownResult
{
    public int StationCount { get; init; }
    public int HandledCount { get; init; }
    public bool SettingsChanged { get; init; }
    public bool AllStationsHandled => HandledCount == StationCount;
}

internal static class BaseStationPowerDownRoutine
{
    public static async Task<BaseStationPowerDownResult> RunAsync(
        BaseStationDevice[] baseStations,
        BaseStationPowerDownMode mode,
        BaseStationGattClient client,
        Action<string> log,
        Action? saveSettings,
        CancellationToken cancellationToken)
    {
        var handledAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackBaseStations = new List<BaseStationDevice>();
        var settingsChanged = false;
        var action = mode == BaseStationPowerDownMode.Standby ? "standby" : "sleep";

        for (var index = 0; index < baseStations.Length; index++)
        {
            var baseStation = baseStations[index];
            cancellationToken.ThrowIfCancellationRequested();

            if (NeedsFallbackShutdownWithoutStateRead(baseStation))
            {
                fallbackBaseStations.Add(baseStation);
                log($"Base station {baseStation.DisplayName}: using two-pass {action} shutdown without status read.");
            }
            else if (await TrySendAndConfirmPowerDownAsync(baseStation, mode, client, log, cancellationToken))
            {
                handledAddresses.Add(baseStation.BluetoothAddress);
            }
            else
            {
                fallbackBaseStations.Add(baseStation);
            }

            if (index < baseStations.Length - 1)
            {
                await Task.Delay(BaseStationCommandTiming.InterStationDelay, cancellationToken);
            }
        }

        for (var pass = 1; pass <= BaseStationCommandTiming.PowerDownFallbackPasses && fallbackBaseStations.Count > 0; pass++)
        {
            log(pass == 1
                ? $"Running two-pass base station {action} shutdown for {fallbackBaseStations.Count} station(s)."
                : $"Repeating base station {action} shutdown pass {pass}/{BaseStationCommandTiming.PowerDownFallbackPasses}.");

            for (var index = 0; index < fallbackBaseStations.Count; index++)
            {
                var baseStation = fallbackBaseStations[index];
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await client.PowerDownAsync(baseStation, mode, cancellationToken);
                    handledAddresses.Add(baseStation.BluetoothAddress);
                    log($"Base station {baseStation.DisplayName}: {action} pass {pass} succeeded.");
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    log($"Base station {baseStation.DisplayName}: could not {action} on pass {pass}: {ex.Message}");
                }

                if (index < fallbackBaseStations.Count - 1)
                {
                    await Task.Delay(BaseStationCommandTiming.InterStationDelay, cancellationToken);
                }
            }
        }

        if (settingsChanged && saveSettings is not null)
        {
            saveSettings();
        }

        return new BaseStationPowerDownResult
        {
            StationCount = baseStations.Length,
            HandledCount = baseStations.Count(baseStation => handledAddresses.Contains(baseStation.BluetoothAddress)),
            SettingsChanged = settingsChanged
        };

        async Task<bool> TrySendAndConfirmPowerDownAsync(
            BaseStationDevice baseStation,
            BaseStationPowerDownMode requestedMode,
            BaseStationGattClient gattClient,
            Action<string> writeLog,
            CancellationToken token)
        {
            try
            {
                await gattClient.PowerDownAsync(baseStation, requestedMode, token);
                writeLog($"Base station {baseStation.DisplayName}: {action} command sent; waiting for status.");
                await Task.Delay(BaseStationCommandTiming.PowerDownStateReadDelay, token);

                var state = await gattClient.ReadPowerStateAsync(baseStation, token);
                if (state == BaseStationPowerState.Unsupported)
                {
                    baseStation.PowerStateReadUnsupported = true;
                    settingsChanged = true;
                    writeLog($"Base station {baseStation.DisplayName}: power-state read is unsupported; using two-pass {action} shutdown.");
                    return false;
                }

                writeLog($"Base station {baseStation.DisplayName}: reported state {state} after {action} command.");
                if (IsConfirmedPowerDownState(state, requestedMode, baseStation))
                {
                    return true;
                }

                writeLog($"Base station {baseStation.DisplayName}: status did not confirm {action}; using two-pass shutdown.");
                return false;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                writeLog($"Base station {baseStation.DisplayName}: could not confirm {action}: {ex.Message}. Using two-pass shutdown.");
                return false;
            }
        }
    }

    private static bool NeedsFallbackShutdownWithoutStateRead(BaseStationDevice baseStation)
        => baseStation.EffectiveVersion == BaseStationVersion.V1 || baseStation.PowerStateReadUnsupported;

    private static bool IsConfirmedPowerDownState(
        BaseStationPowerState state,
        BaseStationPowerDownMode requestedMode,
        BaseStationDevice baseStation)
    {
        var expectedState = requestedMode == BaseStationPowerDownMode.Standby && baseStation.SupportsStandby
            ? BaseStationPowerState.Standby
            : BaseStationPowerState.Sleeping;
        return state == expectedState;
    }
}

internal static class BluetoothAddressConverter
{
    public static string AddressToString(ulong bluetoothAddress)
    {
        var hex = bluetoothAddress.ToString("X012", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        for (var index = 0; index < hex.Length; index += 2)
        {
            if (index > 0)
            {
                builder.Append(':');
            }

            builder.Append(hex.AsSpan(index, 2));
        }

        return builder.ToString();
    }

    public static ulong StringToAddress(string bluetoothAddress)
    {
        var hex = bluetoothAddress.Replace(":", "", StringComparison.Ordinal).Trim();
        return Convert.ToUInt64(hex, 16);
    }
}

internal static class BaseStationDiscovery
{
    public static async Task<bool> HasBluetoothLeAdapterAsync()
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask();
        return adapter is not null && adapter.IsLowEnergySupported;
    }

    public static async Task<IReadOnlyList<BaseStationDevice>> ScanAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (!await HasBluetoothLeAdapterAsync())
        {
            throw new InvalidOperationException("Bluetooth LE adapter not found.");
        }

        var found = new ConcurrentDictionary<string, BaseStationDevice>(StringComparer.OrdinalIgnoreCase);
        string[] requestedProperties = ["System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected"];

        var watchers = new[]
        {
            DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(false),
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint),
            DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                requestedProperties,
                DeviceInformationKind.Device)
        };
        var advertisementWatcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        async void OnAdded(DeviceWatcher _, DeviceInformation deviceInfo)
        {
            var version = BaseStationDevice.InferVersion(deviceInfo.Name);
            if (version == BaseStationVersion.Unknown)
            {
                return;
            }

            try
            {
                var address = TryReadBluetoothAddress(deviceInfo);
                if (string.IsNullOrWhiteSpace(address))
                {
                    using var device = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id).AsTask();
                    if (device is null)
                    {
                        return;
                    }

                    address = BluetoothAddressConverter.AddressToString(device.BluetoothAddress);
                }

                found[address] = new BaseStationDevice
                {
                    FriendlyName = deviceInfo.Name,
                    Name = deviceInfo.Name,
                    BluetoothAddress = address,
                    Version = version,
                    Enabled = true
                };
            }
            catch
            {
                // Discovery should keep scanning even if Windows cannot open one advertisement endpoint.
            }
        }

        void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            var name = eventArgs.Advertisement.LocalName;
            var version = BaseStationDevice.InferVersion(name);
            if (version == BaseStationVersion.Unknown)
            {
                return;
            }

            var address = BluetoothAddressConverter.AddressToString(eventArgs.BluetoothAddress);
            found[address] = new BaseStationDevice
            {
                FriendlyName = name,
                Name = name,
                BluetoothAddress = address,
                Version = version,
                Enabled = true
            };
        }

        foreach (var watcher in watchers)
        {
            watcher.Added += OnAdded;
            watcher.Start();
        }
        advertisementWatcher.Received += OnAdvertisementReceived;
        advertisementWatcher.Start();

        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var watcher in watchers)
            {
                watcher.Added -= OnAdded;
                if (watcher.Status is DeviceWatcherStatus.Created or DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }

            advertisementWatcher.Received -= OnAdvertisementReceived;
            if (advertisementWatcher.Status is BluetoothLEAdvertisementWatcherStatus.Created or BluetoothLEAdvertisementWatcherStatus.Started)
            {
                advertisementWatcher.Stop();
            }
        }

        return found.Values
            .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.BluetoothAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string TryReadBluetoothAddress(DeviceInformation deviceInfo)
    {
        if (!deviceInfo.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value) || value is null)
        {
            return "";
        }

        return value switch
        {
            string text when ulong.TryParse(
                text.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var parsed) => BluetoothAddressConverter.AddressToString(parsed),
            ulong address => BluetoothAddressConverter.AddressToString(address),
            long address => BluetoothAddressConverter.AddressToString((ulong)address),
            _ => ""
        };
    }
}

internal sealed class BaseStationGattClient
{
    private static readonly Guid V1ControlService = new("0000cb00-0000-1000-8000-00805f9b34fb");
    private static readonly Guid V1PowerCharacteristic = new("0000cb01-0000-1000-8000-00805f9b34fb");
    private static readonly Guid V2ControlService = new("00001523-1212-efde-1523-785feabcd124");
    private static readonly Guid V2PowerCharacteristic = new("00001525-1212-efde-1523-785feabcd124");
    private static readonly Guid V2IdentifyCharacteristic = new("00008421-1212-efde-1523-785feabcd124");

    public Task PowerOnAsync(BaseStationDevice baseStation, CancellationToken cancellationToken)
    {
        var version = GetSupportedVersion(baseStation);
        return version == BaseStationVersion.V1
            ? ControlV1Async(baseStation, powerOn: true, cancellationToken)
            : WriteV2PowerCharacteristicAsync(baseStation, 0x01, cancellationToken);
    }

    public Task SleepAsync(BaseStationDevice baseStation, CancellationToken cancellationToken)
    {
        var version = GetSupportedVersion(baseStation);
        return version == BaseStationVersion.V1
            ? ControlV1Async(baseStation, powerOn: false, cancellationToken)
            : WriteV2PowerCharacteristicAsync(baseStation, 0x00, cancellationToken);
    }

    public Task StandbyAsync(BaseStationDevice baseStation, CancellationToken cancellationToken)
    {
        var version = GetSupportedVersion(baseStation);
        if (version == BaseStationVersion.V1)
        {
            throw new InvalidOperationException("Standby is not supported for Base Station 1.0.");
        }

        return WriteV2PowerCharacteristicAsync(baseStation, 0x02, cancellationToken);
    }

    public Task IdentifyAsync(BaseStationDevice baseStation, CancellationToken cancellationToken)
    {
        var version = GetSupportedVersion(baseStation);
        if (version == BaseStationVersion.V1)
        {
            throw new InvalidOperationException("Identify is not supported for Base Station 1.0.");
        }

        return WritePowerCharacteristicAsync(baseStation, V2ControlService, V2IdentifyCharacteristic, [0x01], cancellationToken);
    }

    public Task PowerDownAsync(BaseStationDevice baseStation, BaseStationPowerDownMode mode, CancellationToken cancellationToken)
    {
        return mode == BaseStationPowerDownMode.Standby && baseStation.SupportsStandby
            ? StandbyAsync(baseStation, cancellationToken)
            : SleepAsync(baseStation, cancellationToken);
    }

    public async Task<BaseStationPowerState> ReadPowerStateAsync(BaseStationDevice baseStation, CancellationToken cancellationToken)
    {
        var version = GetSupportedVersion(baseStation);
        if (version == BaseStationVersion.V1)
        {
            return BaseStationPowerState.Unsupported;
        }

        var data = await ReadPowerCharacteristicAsync(baseStation, V2ControlService, V2PowerCharacteristic, cancellationToken);
        if (data is null)
        {
            return BaseStationPowerState.Unsupported;
        }

        if (data.Length == 0)
        {
            return BaseStationPowerState.Unknown;
        }

        return data[0] switch
        {
            0x00 => BaseStationPowerState.Sleeping,
            0x02 => BaseStationPowerState.Standby,
            0x09 or 0x0B => BaseStationPowerState.Awake,
            0x01 => BaseStationPowerState.Waking,
            _ => BaseStationPowerState.Unknown
        };
    }

    private static BaseStationVersion GetSupportedVersion(BaseStationDevice baseStation)
    {
        var version = baseStation.EffectiveVersion;
        if (version == BaseStationVersion.Unknown)
        {
            throw new InvalidOperationException("Base station version is unknown.");
        }

        return version;
    }

    private static Task WriteV2PowerCharacteristicAsync(BaseStationDevice baseStation, byte value, CancellationToken cancellationToken)
        => WritePowerCharacteristicAsync(baseStation, V2ControlService, V2PowerCharacteristic, [value], cancellationToken);

    private static Task ControlV1Async(BaseStationDevice baseStation, bool powerOn, CancellationToken cancellationToken)
    {
        var id = baseStation.Id.Trim();
        if (id.Length != 8)
        {
            throw new InvalidOperationException("Base Station 1.0 requires the 8-character ID printed on the back label.");
        }

        var bytes = powerOn
            ? new byte[] { 0x12, 0x00, 0x00, 0x00 }
            : [0x12, 0x02, 0x00, 0x01];
        var idBytes = Enumerable.Range(0, id.Length / 2)
            .Select(index => Convert.ToByte(id.Substring(index * 2, 2), 16))
            .Reverse();
        var data = bytes
            .Concat(idBytes)
            .Concat(Enumerable.Repeat<byte>(0x00, 12))
            .ToArray();

        return WritePowerCharacteristicAsync(baseStation, V1ControlService, V1PowerCharacteristic, data, cancellationToken);
    }

    private static async Task WritePowerCharacteristicAsync(
        BaseStationDevice baseStation,
        Guid serviceGuid,
        Guid characteristicGuid,
        byte[] data,
        CancellationToken cancellationToken)
    {
        if (!await BaseStationDiscovery.HasBluetoothLeAdapterAsync())
        {
            throw new InvalidOperationException("Bluetooth LE adapter not found.");
        }

        const int retryCount = 10;
        Exception? lastException = null;
        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var device = await GetBluetoothLeDeviceAsync(baseStation.BluetoothAddressValue, cancellationToken);
                using var service = await GetServiceAsync(device, serviceGuid);
                var characteristic = await GetCharacteristicAsync(service, characteristicGuid);
                await WriteCharacteristicAsync(characteristic, data);
                return;
            }
            catch (Exception ex) when (attempt < retryCount && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
            }
        }

        throw new InvalidOperationException($"Could not communicate with {baseStation.DisplayName}.", lastException);
    }

    private static async Task<byte[]?> ReadPowerCharacteristicAsync(
        BaseStationDevice baseStation,
        Guid serviceGuid,
        Guid characteristicGuid,
        CancellationToken cancellationToken)
    {
        if (!await BaseStationDiscovery.HasBluetoothLeAdapterAsync())
        {
            throw new InvalidOperationException("Bluetooth LE adapter not found.");
        }

        const int retryCount = 5;
        Exception? lastException = null;
        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var device = await GetBluetoothLeDeviceAsync(baseStation.BluetoothAddressValue, cancellationToken);
                using var service = await GetServiceAsync(device, serviceGuid);
                var characteristic = await GetCharacteristicAsync(service, characteristicGuid);
                if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                {
                    return null;
                }

                var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask();
                if (result.Status != GattCommunicationStatus.Success)
                {
                    throw new InvalidOperationException($"Could not read Bluetooth characteristic for {characteristic.Service.Device.Name}: {result.Status}.");
                }

                return result.Value.ToArray();
            }
            catch (Exception ex) when (attempt < retryCount && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
            }
        }

        throw new InvalidOperationException($"Could not read power state from {baseStation.DisplayName}.", lastException);
    }

    private static async Task<BluetoothLEDevice> GetBluetoothLeDeviceAsync(ulong address, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask();
            if (device is not null)
            {
                return device;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException("Base station not found.");
    }

    private static async Task<GattDeviceService> GetServiceAsync(BluetoothLEDevice device, Guid serviceGuid)
    {
        var result = await device.GetGattServicesForUuidAsync(serviceGuid, BluetoothCacheMode.Cached);
        if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
        {
            return result.Services[0];
        }

        result = await device.GetGattServicesForUuidAsync(serviceGuid, BluetoothCacheMode.Uncached);
        if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
        {
            return result.Services[0];
        }

        throw new InvalidOperationException($"Could not get Bluetooth service for {device.Name}: {result.Status}.");
    }

    private static async Task<GattCharacteristic> GetCharacteristicAsync(GattDeviceService service, Guid characteristicGuid)
    {
        var result = await service.GetCharacteristicsForUuidAsync(characteristicGuid, BluetoothCacheMode.Cached);
        if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0)
        {
            return result.Characteristics[0];
        }

        result = await service.GetCharacteristicsForUuidAsync(characteristicGuid, BluetoothCacheMode.Uncached);
        if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0)
        {
            return result.Characteristics[0];
        }

        throw new InvalidOperationException($"Could not get Bluetooth characteristic for {service.Device.Name}: {result.Status}.");
    }

    private static async Task WriteCharacteristicAsync(GattCharacteristic characteristic, byte[] data)
    {
        var status = await characteristic.WriteValueAsync(data.AsBuffer());
        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Could not write Bluetooth characteristic for {characteristic.Service.Device.Name}: {status}.");
        }
    }
}
