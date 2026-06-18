using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class PimaxUsbEnumerationSchema
{
    public const string Version = "pimax-usb-enumeration-v1";
}

internal static class PimaxUsbEnumerationJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal sealed record PimaxUsbEnumerationSnapshot(
    string SchemaVersion,
    DateTimeOffset CollectedAt,
    string CollectorVersion,
    PimaxUsbEnumerationHost Host,
    PimaxUsbInventorySummary InventorySummary,
    PimaxUsbDeviceRecord[] CandidateDevices,
    PimaxUsbDeviceRecord[] FullInventory,
    string[] Warnings,
    string[] Errors);

internal sealed record PimaxUsbEnumerationHost(
    string WindowsVersion,
    string Architecture,
    bool IsElevated);

internal sealed record PimaxUsbInventorySummary(
    int TotalDevices,
    int PresentDevices,
    int NonPresentDevices,
    int ProblemDevices,
    IReadOnlyDictionary<string, int> CountsByEnumerator,
    IReadOnlyDictionary<string, int> CountsByClass,
    IReadOnlyDictionary<string, int> CountsByClassGuid,
    IReadOnlyDictionary<string, int> CountsByManufacturer,
    IReadOnlyDictionary<string, int> CountsByVidPid);

internal sealed record PimaxUsbDeviceRecord(
    string StableId,
    string? ParentStableId,
    string? ContainerStableId,
    string EnumeratorName,
    bool Present,
    bool Connected,
    bool Phantom,
    string? DeviceClass,
    string? ClassGuid,
    string? FriendlyName,
    string? DeviceDescription,
    string? Manufacturer,
    string? Service,
    string? DriverProvider,
    string? DriverVersion,
    string? DriverDate,
    string? Status,
    int? ProblemCode,
    string? ConfigManagerStatus,
    string[] HardwareIds,
    string[] CompatibleIds,
    string? Vid,
    string? Pid,
    string? Revision,
    string? UsbInterfaceNumber,
    string? LocationInformation,
    string[] LocationPathHashes,
    string[] CandidateReasons,
    string[] PropertyQueryFailures,
    string EvidenceSource);

internal sealed record PimaxUsbRawDeviceRecord(
    string InstanceId,
    string? ParentInstanceId,
    string? ContainerId,
    string EnumeratorName,
    bool Present,
    bool Connected,
    bool Phantom,
    string? DeviceClass,
    string? ClassGuid,
    string? FriendlyName,
    string? DeviceDescription,
    string? Manufacturer,
    string? Service,
    string? DriverProvider,
    string? DriverVersion,
    string? DriverDate,
    string? Status,
    int? ProblemCode,
    string? ConfigManagerStatus,
    string[] HardwareIds,
    string[] CompatibleIds,
    string? Vid,
    string? Pid,
    string? Revision,
    string? UsbInterfaceNumber,
    string? LocationInformation,
    string[] LocationPaths,
    string[] PropertyQueryFailures,
    string EvidenceSource,
    string? DriverKey = null);

internal sealed record PimaxUsbInventoryResult(
    PimaxUsbRawDeviceRecord[] Devices,
    string[] Warnings,
    string[] Errors);

internal interface IPimaxUsbDeviceInventorySource
{
    PimaxUsbInventoryResult Collect();
}

internal sealed class PimaxUsbEnumerationSnapshotCollector
{
    private readonly IPimaxUsbDeviceInventorySource _inventorySource;

    public PimaxUsbEnumerationSnapshotCollector()
        : this(new WindowsPnpDeviceInventorySource())
    {
    }

    internal PimaxUsbEnumerationSnapshotCollector(IPimaxUsbDeviceInventorySource inventorySource)
    {
        _inventorySource = inventorySource;
    }

    public PimaxUsbEnumerationSnapshot Collect()
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var raw = Array.Empty<PimaxUsbRawDeviceRecord>();

        try
        {
            var result = _inventorySource.Collect();
            raw = result.Devices;
            warnings.AddRange(result.Warnings);
            errors.AddRange(result.Errors);
        }
        catch (Exception ex)
        {
            errors.Add($"USB/PnP inventory collection failed: {ex.Message}");
        }

        var records = raw
            .Select(rawDevice => PimaxUsbDeviceNormalizer.ToSanitizedRecord(rawDevice))
            .OrderBy(record => record.EnumeratorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PimaxUsbEnumerationSnapshot(
            PimaxUsbEnumerationSchema.Version,
            DateTimeOffset.Now,
            typeof(PimaxUsbEnumerationSnapshotCollector).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(PimaxUsbEnumerationSnapshotCollector).Assembly.GetName().Version?.ToString()
                ?? "unknown",
            BuildHost(),
            PimaxUsbInventorySummaryBuilder.Build(records),
            records.Where(record => record.CandidateReasons.Length > 0).ToArray(),
            records,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static PimaxUsbEnumerationHost BuildHost()
    {
        var isElevated = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            isElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            isElevated = false;
        }

        return new PimaxUsbEnumerationHost(
            Environment.OSVersion.VersionString,
            RuntimeInformation.OSArchitecture.ToString(),
            isElevated);
    }
}

internal static class PimaxUsbInventorySummaryBuilder
{
    public static PimaxUsbInventorySummary Build(IReadOnlyCollection<PimaxUsbDeviceRecord> devices)
        => new(
            devices.Count,
            devices.Count(device => device.Present),
            devices.Count(device => !device.Present),
            devices.Count(device => device.ProblemCode is > 0 || string.Equals(device.ConfigManagerStatus, "Problem", StringComparison.OrdinalIgnoreCase)),
            CountBy(devices, device => device.EnumeratorName),
            CountBy(devices, device => device.DeviceClass),
            CountBy(devices, device => device.ClassGuid),
            CountBy(devices, device => device.Manufacturer),
            CountBy(devices, device => string.IsNullOrWhiteSpace(device.Vid) || string.IsNullOrWhiteSpace(device.Pid) ? null : $"VID_{device.Vid}&PID_{device.Pid}"));

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<PimaxUsbDeviceRecord> records, Func<PimaxUsbDeviceRecord, string?> selector)
        => records
            .Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
}

internal static class PimaxUsbDeviceNormalizer
{
    public static PimaxUsbDeviceRecord ToSanitizedRecord(PimaxUsbRawDeviceRecord raw)
    {
        var reasons = PimaxUsbCandidateSelector.GetCandidateReasons(raw);
        return new PimaxUsbDeviceRecord(
            PnpIdentitySanitizer.StableHash(raw.InstanceId),
            PnpIdentitySanitizer.StableHashOrNull(raw.ParentInstanceId),
            PnpIdentitySanitizer.StableHashOrNull(raw.ContainerId),
            raw.EnumeratorName,
            raw.Present,
            raw.Connected,
            raw.Phantom,
            raw.DeviceClass,
            raw.ClassGuid,
            raw.FriendlyName,
            raw.DeviceDescription,
            raw.Manufacturer,
            raw.Service,
            raw.DriverProvider,
            raw.DriverVersion,
            raw.DriverDate,
            raw.Status,
            raw.ProblemCode,
            raw.ConfigManagerStatus,
            raw.HardwareIds,
            raw.CompatibleIds,
            raw.Vid,
            raw.Pid,
            raw.Revision,
            raw.UsbInterfaceNumber,
            raw.LocationInformation,
            raw.LocationPaths.Select(PnpIdentitySanitizer.StableHash).ToArray(),
            reasons,
            raw.PropertyQueryFailures,
            raw.EvidenceSource);
    }
}

internal static class PnpIdentitySanitizer
{
    public static string StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string? StableHashOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : StableHash(value);
}

internal static partial class PimaxUsbCandidateSelector
{
    public static string[] GetCandidateReasons(PimaxUsbRawDeviceRecord record)
    {
        var reasons = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = string.Join(
            "\n",
            record.InstanceId,
            record.ParentInstanceId,
            record.DeviceClass,
            record.FriendlyName,
            record.DeviceDescription,
            record.Manufacturer,
            record.Service,
            string.Join("\n", record.HardwareIds),
            string.Join("\n", record.CompatibleIds),
            record.LocationInformation,
            string.Join("\n", record.LocationPaths));

        if (text.Contains("Pimax", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("knownPimaxName");
        }

        if (text.Contains("Crystal", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("knownCrystalName");
        }

        if (string.Equals(record.Vid, "34A4", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(string.Equals(record.Pid, "0012", StringComparison.OrdinalIgnoreCase)
                ? "knownCrystalVidPid"
                : "knownPimaxVid");
        }

        if (record.EnumeratorName.Equals("USB", StringComparison.OrdinalIgnoreCase)
            && IsRelevantUsbClass(record.DeviceClass))
        {
            reasons.Add("usbDeviceWithRelevantInterfaceClass");
        }

        if (record.EnumeratorName.Equals("HID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.DeviceClass, "HIDClass", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("relatedHidInterface");
        }

        if (IsAudioClass(record.DeviceClass) || ContainsAny(text, "AC Interface", "Pimax Streaming Microphone"))
        {
            reasons.Add("relatedAudioInterface");
        }

        if (string.Equals(record.DeviceClass, "Camera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.DeviceClass, "Image", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("relatedCameraInterface");
        }

        if (string.Equals(record.DeviceClass, "Sensor", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("relatedSensorInterface");
        }

        if (ContainsAny(text, "bootloader", "firmware", "dfu", "recovery"))
        {
            reasons.Add("possibleBootloaderIdentity");
        }

        if (record.ProblemCode is > 0 && IsUsbLike(record))
        {
            reasons.Add("problemUsbOrPnpDevice");
        }

        return reasons.ToArray();
    }

    private static bool IsRelevantUsbClass(string? deviceClass)
        => deviceClass is not null
            && (deviceClass.Equals("USB", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("HIDClass", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("Camera", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("Image", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("Sensor", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("Unknown", StringComparison.OrdinalIgnoreCase));

    private static bool IsAudioClass(string? deviceClass)
        => deviceClass is not null
            && (deviceClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase)
                || deviceClass.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase));

    private static bool IsUsbLike(PimaxUsbRawDeviceRecord record)
        => record.EnumeratorName.Equals("USB", StringComparison.OrdinalIgnoreCase)
            || record.InstanceId.Contains("USB", StringComparison.OrdinalIgnoreCase)
            || record.HardwareIds.Any(id => id.Contains("USB", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}

internal sealed class WindowsPnpDeviceInventorySource : IPimaxUsbDeviceInventorySource
{
    public PimaxUsbInventoryResult Collect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PimaxUsbInventoryResult([], ["USB/PnP enumeration is Windows-only."], []);
        }

        var devices = new List<PimaxUsbRawDeviceRecord>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, NativeMethods.DIGCF_ALLCLASSES);
        if (deviceInfoSet == NativeMethods.InvalidHandleValue)
        {
            return new PimaxUsbInventoryResult([], [], [$"SetupDiGetClassDevs failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}"]);
        }

        try
        {
            var index = 0u;
            while (true)
            {
                var data = NativeMethods.SP_DEVINFO_DATA.Create();
                if (!NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    errors.Add($"SetupDiEnumDeviceInfo({index}) failed: {new Win32Exception(error).Message}");
                    break;
                }

                var failures = new List<string>();
                var instanceId = GetDeviceId(data.DevInst, failures);
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    devices.Add(BuildRecord(deviceInfoSet, data, instanceId, failures));
                }
                else
                {
                    warnings.Add($"Skipped device index {index}: device instance ID unavailable.");
                }

                index++;
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return new PimaxUsbInventoryResult(devices.ToArray(), warnings.ToArray(), errors.ToArray());
    }

    private static PimaxUsbRawDeviceRecord BuildRecord(IntPtr deviceInfoSet, NativeMethods.SP_DEVINFO_DATA data, string instanceId, List<string> failures)
    {
        var status = GetStatus(data.DevInst, failures);
        var hardwareIds = GetRegistryStringList(deviceInfoSet, data, NativeMethods.SPDRP_HARDWAREID, "hardwareIds", failures);
        var compatibleIds = GetRegistryStringList(deviceInfoSet, data, NativeMethods.SPDRP_COMPATIBLEIDS, "compatibleIds", failures);
        var parentId = GetParentDeviceId(data.DevInst, failures);
        var containerId = GetDevicePropertyString(deviceInfoSet, data, NativeMethods.DEVPKEY_Device_ContainerId, "containerId", failures);
        var locationPaths = GetRegistryStringList(deviceInfoSet, data, NativeMethods.SPDRP_LOCATION_PATHS, "locationPaths", failures);
        var allIds = new[] { instanceId }
            .Concat(hardwareIds)
            .Concat(compatibleIds)
            .ToArray();

        return new PimaxUsbRawDeviceRecord(
            instanceId,
            parentId,
            containerId,
            GetEnumeratorName(instanceId),
            status.Present,
            status.Connected,
            status.Phantom,
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_CLASS, "class", failures),
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_CLASSGUID, "classGuid", failures),
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_FRIENDLYNAME, "friendlyName", failures),
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_DEVICEDESC, "deviceDescription", failures),
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_MFG, "manufacturer", failures),
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_SERVICE, "service", failures),
            null,
            null,
            null,
            status.Status,
            status.ProblemCode,
            status.ConfigManagerStatus,
            hardwareIds,
            compatibleIds,
            ExtractIdPart(allIds, "VID_"),
            ExtractIdPart(allIds, "PID_"),
            ExtractIdPart(allIds, "REV_"),
            ExtractMi(allIds),
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_LOCATION_INFORMATION, "locationInformation", failures),
            locationPaths,
            failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            "SetupAPI/ConfigurationManager",
            GetRegistryString(deviceInfoSet, data, NativeMethods.SPDRP_DRIVER, "driverKey", failures));
    }

    private static string GetDeviceId(uint devInst, List<string> failures)
    {
        var buffer = new StringBuilder(1024);
        var result = NativeMethods.CM_Get_Device_IDW(devInst, buffer, buffer.Capacity, 0);
        if (result != NativeMethods.CR_SUCCESS)
        {
            failures.Add($"deviceId: CM_Get_Device_ID failed with 0x{result:X8}");
            return "";
        }

        return buffer.ToString();
    }

    private static string? GetParentDeviceId(uint devInst, List<string> failures)
    {
        var result = NativeMethods.CM_Get_Parent(out var parent, devInst, 0);
        if (result != NativeMethods.CR_SUCCESS)
        {
            failures.Add($"parent: CM_Get_Parent failed with 0x{result:X8}");
            return null;
        }

        return GetDeviceId(parent, failures);
    }

    private static (bool Present, bool Connected, bool Phantom, string Status, int? ProblemCode, string ConfigManagerStatus) GetStatus(uint devInst, List<string> failures)
    {
        var result = NativeMethods.CM_Get_DevNode_Status(out var statusFlags, out var problemNumber, devInst, 0);
        if (result != NativeMethods.CR_SUCCESS)
        {
            failures.Add($"status: CM_Get_DevNode_Status failed with 0x{result:X8}");
            return (false, false, true, "Unavailable", null, "Unavailable");
        }

        var phantom = (statusFlags & NativeMethods.DN_PHANTOM) != 0;
        var started = (statusFlags & NativeMethods.DN_STARTED) != 0;
        var hasProblem = (statusFlags & NativeMethods.DN_HAS_PROBLEM) != 0 || problemNumber > 0;
        var present = !phantom;
        var status = hasProblem
            ? "Problem"
            : started
                ? "Started"
                : present
                    ? "Present"
                    : "NonPresent";

        return (present, present && started && !hasProblem, phantom, status, problemNumber > 0 ? (int)problemNumber : null, status);
    }

    private static string? GetRegistryString(IntPtr deviceInfoSet, NativeMethods.SP_DEVINFO_DATA data, uint property, string name, List<string> failures)
        => GetRegistryProperty(deviceInfoSet, data, property, name, failures).FirstOrDefault();

    private static string[] GetRegistryStringList(IntPtr deviceInfoSet, NativeMethods.SP_DEVINFO_DATA data, uint property, string name, List<string> failures)
        => GetRegistryProperty(deviceInfoSet, data, property, name, failures);

    private static string[] GetRegistryProperty(IntPtr deviceInfoSet, NativeMethods.SP_DEVINFO_DATA data, uint property, string name, List<string> failures)
    {
        var buffer = new byte[8192];
        if (!NativeMethods.SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref data, property, out _, buffer, buffer.Length, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is NativeMethods.ERROR_INVALID_DATA or NativeMethods.ERROR_NOT_FOUND)
            {
                return [];
            }

            failures.Add($"{name}: SetupDiGetDeviceRegistryProperty failed: {new Win32Exception(error).Message}");
            return [];
        }

        return DecodeStringBuffer(buffer, (int)requiredSize);
    }

    private static string? GetDevicePropertyString(IntPtr deviceInfoSet, NativeMethods.SP_DEVINFO_DATA data, NativeMethods.DEVPROPKEY propertyKey, string name, List<string> failures)
    {
        var buffer = new byte[1024];
        if (!NativeMethods.SetupDiGetDevicePropertyW(deviceInfoSet, ref data, ref propertyKey, out var propertyType, buffer, buffer.Length, out var requiredSize, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is NativeMethods.ERROR_NOT_FOUND or NativeMethods.ERROR_INVALID_DATA)
            {
                return null;
            }

            failures.Add($"{name}: SetupDiGetDeviceProperty failed: {new Win32Exception(error).Message}");
            return null;
        }

        if (propertyType == NativeMethods.DEVPROP_TYPE_GUID && requiredSize >= 16)
        {
            return new Guid(buffer.AsSpan(0, 16)).ToString("D");
        }

        return DecodeStringBuffer(buffer, (int)requiredSize).FirstOrDefault();
    }

    private static string[] DecodeStringBuffer(byte[] buffer, int byteCount)
    {
        var text = Encoding.Unicode.GetString(buffer, 0, Math.Min(buffer.Length, byteCount)).TrimEnd('\0');
        return text
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetEnumeratorName(string instanceId)
    {
        var index = instanceId.IndexOf('\\');
        return index > 0 ? instanceId[..index] : instanceId;
    }

    private static string? ExtractIdPart(IEnumerable<string> values, string prefix)
    {
        var pattern = Regex.Escape(prefix) + "([0-9A-Fa-f]{4})";
        foreach (var value in values)
        {
            var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }
        }

        return null;
    }

    private static string? ExtractMi(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            var match = Regex.Match(value, @"MI_([0-9A-Fa-f]{2})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }
        }

        return null;
    }

    private static class NativeMethods
    {
        public static readonly IntPtr InvalidHandleValue = new(-1);

        public const int DIGCF_ALLCLASSES = 0x00000004;
        public const int ERROR_NO_MORE_ITEMS = 259;
        public const int ERROR_INVALID_DATA = 13;
        public const int ERROR_NOT_FOUND = 1168;
        public const uint CR_SUCCESS = 0x00000000;
        public const uint DN_STARTED = 0x00000008;
        public const uint DN_PHANTOM = 0x00000020;
        public const uint DN_HAS_PROBLEM = 0x00000400;
        public const uint DEVPROP_TYPE_GUID = 0x0000000D;

        public const uint SPDRP_DEVICEDESC = 0x00000000;
        public const uint SPDRP_HARDWAREID = 0x00000001;
        public const uint SPDRP_COMPATIBLEIDS = 0x00000002;
        public const uint SPDRP_SERVICE = 0x00000004;
        public const uint SPDRP_DRIVER = 0x00000009;
        public const uint SPDRP_CLASS = 0x00000007;
        public const uint SPDRP_CLASSGUID = 0x00000008;
        public const uint SPDRP_MFG = 0x0000000B;
        public const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        public const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;
        public const uint SPDRP_LOCATION_PATHS = 0x00000023;

        public static DEVPROPKEY DEVPKEY_Device_ContainerId = new(
            new Guid(0x8c7ed206, 0x3f8a, 0x4827, 0xb3, 0xab, 0xae, 0x9e, 0x1f, 0xae, 0xfc, 0x6c),
            2);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;

            public static SP_DEVINFO_DATA Create()
                => new()
                {
                    cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>()
                };
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY(Guid fmtid, uint pid)
        {
            public Guid Fmtid = fmtid;
            public uint Pid = pid;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevsW(
            IntPtr classGuid,
            string? enumerator,
            IntPtr hwndParent,
            int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            int propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDevicePropertyW(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            ref DEVPROPKEY propertyKey,
            out uint propertyType,
            byte[] propertyBuffer,
            int propertyBufferSize,
            out uint requiredSize,
            uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Get_Device_IDW(
            uint dnDevInst,
            StringBuilder buffer,
            int bufferLen,
            uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        public static extern uint CM_Get_Parent(
            out uint pdnDevInst,
            uint dnDevInst,
            uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        public static extern uint CM_Get_DevNode_Status(
            out uint pulStatus,
            out uint pulProblemNumber,
            uint dnDevInst,
            uint ulFlags);
    }
}
