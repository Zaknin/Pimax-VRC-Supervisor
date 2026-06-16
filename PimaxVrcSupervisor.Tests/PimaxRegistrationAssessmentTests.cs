using System.Text.Json;
using Xunit;

public sealed class PimaxRegistrationAssessmentTests
{
    [Fact]
    public void HeadsetOffEvidenceProducesLikelyHeadsetOff()
    {
        var result = Assess(Filtered(PimaxConnectivityAssessmentValue.WindowsDevicesAbsent), Usb([]));

        Assert.Equal(PimaxRegistrationState.LikelyHeadsetOff, result.State);
        Assert.Equal(PimaxRegistrationConfidence.Probable, result.Confidence);
    }

    [Fact]
    public void PowerOnWithoutRuntimeProducesAwaitingRegistration()
    {
        var result = Assess(
            Filtered(PimaxConnectivityAssessmentValue.WindowsDevicesAbsent),
            Usb(PowerOnGroup()));

        Assert.Equal(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration, result.State);
        Assert.True(result.Evidence.HeadsetPowerOnGroupPresent);
        Assert.False(result.Evidence.CrystalRuntimeGroupPresent);
    }

    [Fact]
    public void RuntimeGroupAndFilteredConnectedProducesRegisteredReady()
    {
        var result = Assess(
            Filtered(PimaxConnectivityAssessmentValue.Connected),
            Usb(PowerOnGroup().Concat(RuntimeGroup()).ToArray()));

        Assert.Equal(PimaxRegistrationState.RegisteredReady, result.State);
        Assert.Equal(PimaxRegistrationConfidence.Confirmed, result.Confidence);
        Assert.True(result.Evidence.FilteredExpandedAgreement);
    }

    [Fact]
    public void PartialRuntimeGroupDoesNotConfirmRegisteredReady()
    {
        var result = Assess(
            Filtered(PimaxConnectivityAssessmentValue.WindowsDevicesPresentRuntimeNotConfirmed),
            Usb(PowerOnGroup().Concat(RuntimeGroup().Take(2)).ToArray()));

        Assert.Equal(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration, result.State);
        Assert.False(result.Evidence.CrystalRuntimeGroupPresent);
    }

    [Fact]
    public void FilteredConnectedWithoutExpandedRuntimeProducesConflict()
    {
        var result = Assess(
            Filtered(PimaxConnectivityAssessmentValue.Connected),
            Usb(PowerOnGroup()));

        Assert.Equal(PimaxRegistrationState.ConflictingEvidence, result.State);
        Assert.NotEmpty(result.Conflicts);
    }

    [Fact]
    public void UnknownEvidenceProducesInsufficientConfidence()
    {
        var result = Assess(
            Filtered(PimaxConnectivityAssessmentValue.InsufficientEvidence),
            Usb([Device("USB", "9999", "8888")]));

        Assert.Equal(PimaxRegistrationState.Unknown, result.State);
        Assert.Equal(PimaxRegistrationConfidence.Insufficient, result.Confidence);
    }

    [Fact]
    public void ReorderedInventoriesProduceSameResult()
    {
        var ordered = PowerOnGroup().Concat(RuntimeGroup()).ToArray();
        var reversed = ordered.Reverse().ToArray();

        var first = Assess(Filtered(PimaxConnectivityAssessmentValue.Connected), Usb(ordered));
        var second = Assess(Filtered(PimaxConnectivityAssessmentValue.Connected), Usb(reversed));

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Evidence.CrystalRuntimeGroupStartedRecords, second.Evidence.CrystalRuntimeGroupStartedRecords);
    }

    [Fact]
    public void DuplicateAndUnexpectedExtraUsbDevicesDoNotBlockRegisteredReady()
    {
        var records = PowerOnGroup()
            .Concat(RuntimeGroup())
            .Concat([RuntimeGroup()[0], Device("USB", "9999", "8888")])
            .ToArray();

        var result = Assess(Filtered(PimaxConnectivityAssessmentValue.Connected), Usb(records));

        Assert.Equal(PimaxRegistrationState.RegisteredReady, result.State);
    }

    [Fact]
    public void ProblemCodeRecordDoesNotCountAsStartedEvidence()
    {
        var problem = Device("USB", "28DE", "2300", present: true, connected: true, phantom: false, status: "Problem", problemCode: 10);
        var result = Assess(Filtered(PimaxConnectivityAssessmentValue.WindowsDevicesAbsent), Usb([problem]));

        Assert.Equal(PimaxRegistrationState.LikelyHeadsetOff, result.State);
        Assert.False(result.Evidence.HeadsetPowerOnGroupPresent);
    }

    [Fact]
    public void ExcessiveCollectionGapProducesStructuredWarning()
    {
        var result = Assess(
            Filtered(PimaxConnectivityAssessmentValue.Connected),
            Usb(PowerOnGroup().Concat(RuntimeGroup()).ToArray()),
            collectionGapMs: 5_000);

        Assert.Contains(result.Warnings, warning => warning.Contains("collected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CoordinatorAggregatesProbeFailureAsUnknownWithoutThrowing()
    {
        var coordinator = new PimaxRegistrationAssessmentCoordinator(
            (_, _) => Task.FromException<PimaxConnectivitySnapshot>(new InvalidOperationException("filtered failed")),
            () => Usb(PowerOnGroup()),
            new PimaxRegistrationStateAssessor());

        var snapshot = await coordinator.CollectAsync(null!, CancellationToken.None);

        Assert.Equal(PimaxRegistrationState.Unknown, snapshot.Assessment.State);
        Assert.Contains(snapshot.Errors, error => error.Contains("filtered failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CoordinatorReportsCancellation()
    {
        var coordinator = new PimaxRegistrationAssessmentCoordinator(
            (_, _) => Task.FromCanceled<PimaxConnectivitySnapshot>(new CancellationToken(true)),
            () => Usb(PowerOnGroup()),
            new PimaxRegistrationStateAssessor());

        var snapshot = await coordinator.CollectAsync(null!, CancellationToken.None);

        Assert.Equal(PimaxRegistrationState.Unknown, snapshot.Assessment.State);
        Assert.Contains(snapshot.Errors, error => error.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CoordinatorReportsUsbProbeFailure()
    {
        var coordinator = new PimaxRegistrationAssessmentCoordinator(
            (_, _) => Task.FromResult(Filtered(PimaxConnectivityAssessmentValue.WindowsDevicesAbsent)),
            () => throw new InvalidOperationException("usb failed"),
            new PimaxRegistrationStateAssessor());

        var snapshot = await coordinator.CollectAsync(null!, CancellationToken.None);

        Assert.Equal(PimaxRegistrationState.Unknown, snapshot.Assessment.State);
        Assert.Contains(snapshot.Errors, error => error.Contains("usb failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SchemasRemainStable()
    {
        Assert.Equal("pimax-connectivity-v1", PimaxConnectivitySchema.Version);
        Assert.Equal("pimax-usb-enumeration-v1", PimaxUsbEnumerationSchema.Version);
        Assert.Equal("pimax-registration-assessment-v1", PimaxRegistrationAssessmentSchema.Version);
    }

    [Fact]
    public void RegistrationAssessmentSerializesExpectedFields()
    {
        var snapshot = new PimaxRegistrationAssessmentSnapshot(
            PimaxRegistrationAssessmentSchema.Version,
            DateTimeOffset.Now,
            Assess(Filtered(PimaxConnectivityAssessmentValue.Connected), Usb(PowerOnGroup().Concat(RuntimeGroup()).ToArray())),
            new PimaxRegistrationSourceSchemaVersions(PimaxConnectivitySchema.Version, PimaxUsbEnumerationSchema.Version),
            new PimaxRegistrationSnapshotMetadata(PimaxConnectivitySchema.Version, DateTimeOffset.Now, 1, "connected", "confirmed", 0, 0),
            new PimaxRegistrationSnapshotMetadata(PimaxUsbEnumerationSchema.Version, DateTimeOffset.Now, null, "inventory", "probable", 0, 0),
            3,
            [],
            []);

        var json = JsonSerializer.Serialize(snapshot, PimaxRegistrationAssessmentJson.Options);

        Assert.Contains("\"schemaVersion\":\"pimax-registration-assessment-v1\"", json);
        Assert.Contains("\"assessment\"", json);
        Assert.Contains("\"collectionGapMs\"", json);
        Assert.Contains("\"sourceSchemaVersions\"", json, StringComparison.OrdinalIgnoreCase);
    }

    private static PimaxRegistrationAssessmentResult Assess(
        PimaxConnectivitySnapshot filtered,
        PimaxUsbEnumerationSnapshot expanded,
        double collectionGapMs = 25)
        => new PimaxRegistrationStateAssessor().Evaluate(filtered, expanded, collectionGapMs);

    private static PimaxConnectivitySnapshot Filtered(string value)
    {
        var assessment = new PimaxConnectivityAssessmentResult(
            value,
            value == PimaxConnectivityAssessmentValue.Connected ? PimaxConnectivityConfidence.Confirmed : PimaxConnectivityConfidence.Probable,
            "synthetic",
            [],
            [],
            []);
        return new PimaxConnectivitySnapshot(
            PimaxConnectivitySchema.Version,
            DateTimeOffset.Now,
            1,
            new PimaxInstallationObservation(PimaxProbeStatus.Available, [], [], [], []),
            new PimaxProcessObservation(PimaxProbeStatus.Available, [], [], []),
            new PimaxServiceObservation(PimaxProbeStatus.Available, [], [], []),
            new PimaxDeviceObservation(PimaxProbeStatus.Available, [], [], false, false, false, [], [], []),
            new PimaxRuntimeEvidenceObservation(PimaxProbeStatus.Inconclusive, DateTimeOffset.Now, 0, [], null, null, [], []),
            new PimaxSteamVrDriverObservation(PimaxProbeStatus.Inconclusive, [], false, [], []),
            assessment,
            assessment.Confidence,
            [],
            []);
    }

    private static PimaxUsbEnumerationSnapshot Usb(PimaxUsbDeviceRecord[] records)
        => new(
            PimaxUsbEnumerationSchema.Version,
            DateTimeOffset.Now.AddMilliseconds(20),
            "test",
            new PimaxUsbEnumerationHost("test", "x64", false),
            PimaxUsbInventorySummaryBuilder.Build(records),
            records.Where(record => record.CandidateReasons.Length > 0).ToArray(),
            records,
            [],
            []);

    private static PimaxUsbDeviceRecord[] PowerOnGroup()
        =>
        [
            Device("USB", "05E3", "0608", "Generic USB Hub", "USB"),
            Device("USB", "28DE", "2101", "USB Input Device", "HIDClass"),
            Device("USB", "28DE", "2300", "USB Composite Device", "USB"),
            Device("HID", "28DE", "2300", "HID-compliant vendor-defined device", "HIDClass", mi: "00")
        ];

    private static PimaxUsbDeviceRecord[] RuntimeGroup()
        =>
        [
            Device("USB", "34A4", "0012", "USB Composite Device", "USB"),
            Device("USB", "34A4", "0012", "UVC Camera", "Camera", mi: "00"),
            Device("USB", "34A4", "0012", "USB Input Device", "HIDClass", mi: "02"),
            Device("USB", "34A4", "0012", "AC Interface", "MEDIA", mi: "03"),
            Device("SWD", null, null, "Pimax Streaming Microphone (AC Interface)", "AudioEndpoint"),
            Device("USB", "2104", "0220", "EyeChip", "USBDevice")
        ];

    private static PimaxUsbDeviceRecord Device(
        string enumerator,
        string? vid,
        string? pid,
        string name = "Synthetic Device",
        string deviceClass = "USB",
        bool present = true,
        bool connected = true,
        bool phantom = false,
        string status = "Started",
        int? problemCode = null,
        string? mi = null)
    {
        var hardwareIds = string.IsNullOrWhiteSpace(vid) || string.IsNullOrWhiteSpace(pid)
            ? Array.Empty<string>()
            : mi is null
                ? [$"USB\\VID_{vid}&PID_{pid}"]
                : [$"USB\\VID_{vid}&PID_{pid}&MI_{mi}"];
        var raw = new PimaxUsbRawDeviceRecord(
            $"{enumerator}\\{(vid is null || pid is null ? name : $"VID_{vid}&PID_{pid}")}\\SYNTHETIC-{mi ?? "ROOT"}",
            null,
            "synthetic-container",
            enumerator,
            present,
            connected,
            phantom,
            deviceClass,
            "{SYNTHETIC-CLASS}",
            name,
            name,
            "Synthetic",
            null,
            null,
            null,
            null,
            status,
            problemCode,
            problemCode is > 0 ? "Problem" : status,
            hardwareIds,
            [],
            vid,
            pid,
            null,
            mi,
            "synthetic location",
            [],
            [],
            "Synthetic");
        return PimaxUsbDeviceNormalizer.ToSanitizedRecord(raw);
    }
}
