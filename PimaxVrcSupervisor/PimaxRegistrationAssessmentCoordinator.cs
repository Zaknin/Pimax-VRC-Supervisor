using System.Diagnostics;

internal sealed class PimaxRegistrationAssessmentCoordinator
{
    private readonly Func<SupervisorConfig, CancellationToken, Task<PimaxConnectivitySnapshot>> _collectConnectivity;
    private readonly Func<PimaxUsbEnumerationSnapshot> _collectUsb;
    private readonly PimaxRegistrationStateAssessor _assessor;

    public PimaxRegistrationAssessmentCoordinator()
        : this(
            (config, cancellationToken) => new PimaxConnectivitySnapshotCollector().CollectAsync(config, cancellationToken),
            () => new PimaxUsbEnumerationSnapshotCollector().Collect(),
            new PimaxRegistrationStateAssessor())
    {
    }

    internal PimaxRegistrationAssessmentCoordinator(
        Func<SupervisorConfig, CancellationToken, Task<PimaxConnectivitySnapshot>> collectConnectivity,
        Func<PimaxUsbEnumerationSnapshot> collectUsb,
        PimaxRegistrationStateAssessor assessor)
    {
        _collectConnectivity = collectConnectivity;
        _collectUsb = collectUsb;
        _assessor = assessor;
    }

    public async Task<PimaxRegistrationAssessmentSnapshot> CollectAsync(
        SupervisorConfig config,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var startedAt = Stopwatch.GetTimestamp();
        PimaxConnectivitySnapshot? filtered = null;
        PimaxUsbEnumerationSnapshot? expanded = null;

        try
        {
            filtered = await _collectConnectivity(config, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            errors.Add("Filtered Pimax connectivity collection was canceled.");
        }
        catch (Exception ex)
        {
            errors.Add($"Filtered Pimax connectivity collection failed: {ex.Message}");
        }

        try
        {
            expanded = _collectUsb();
        }
        catch (Exception ex)
        {
            errors.Add($"Expanded USB/PnP inventory collection failed: {ex.Message}");
        }

        var collectedAt = DateTimeOffset.Now;
        if (filtered is null || expanded is null)
        {
            var emptyAssessment = new PimaxRegistrationAssessmentResult(
                PimaxRegistrationState.Unknown,
                PimaxRegistrationConfidence.Insufficient,
                "One or more required read-only probes failed.",
                [],
                [],
                filtered is null && expanded is null
                    ? ["Filtered connectivity and expanded USB/PnP evidence are unavailable."]
                    : filtered is null
                        ? ["Filtered connectivity evidence is unavailable."]
                        : ["Expanded USB/PnP evidence is unavailable."],
                [],
                [],
                new PimaxRegistrationEvidence(false, 0, 0, false, 0, 0, false, false, false, false, false, [], []));

            return new PimaxRegistrationAssessmentSnapshot(
                PimaxRegistrationAssessmentSchema.Version,
                collectedAt,
                emptyAssessment,
                SourceSchemas(filtered, expanded),
                Metadata(filtered),
                Metadata(expanded),
                0,
                warnings.ToArray(),
                errors.ToArray());
        }

        var gap = Math.Abs((expanded.CollectedAt - filtered.CollectedAt).TotalMilliseconds);
        var assessment = _assessor.Evaluate(filtered, expanded, gap);
        warnings.AddRange(filtered.Warnings);
        warnings.AddRange(expanded.Warnings);
        warnings.AddRange(assessment.Warnings);
        errors.AddRange(filtered.Errors);
        errors.AddRange(expanded.Errors);

        return new PimaxRegistrationAssessmentSnapshot(
            PimaxRegistrationAssessmentSchema.Version,
            collectedAt,
            assessment,
            SourceSchemas(filtered, expanded),
            Metadata(filtered),
            Metadata(expanded),
            gap,
            warnings.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static PimaxRegistrationSourceSchemaVersions SourceSchemas(
        PimaxConnectivitySnapshot? filtered,
        PimaxUsbEnumerationSnapshot? expanded)
        => new(
            filtered?.SchemaVersion ?? PimaxConnectivitySchema.Version,
            expanded?.SchemaVersion ?? PimaxUsbEnumerationSchema.Version);

    private static PimaxRegistrationSnapshotMetadata Metadata(PimaxConnectivitySnapshot? snapshot)
        => snapshot is null
            ? new PimaxRegistrationSnapshotMetadata(
                PimaxConnectivitySchema.Version,
                DateTimeOffset.MinValue,
                null,
                "unavailable",
                PimaxRegistrationConfidence.Insufficient,
                0,
                1)
            : new PimaxRegistrationSnapshotMetadata(
                snapshot.SchemaVersion,
                snapshot.CollectedAt,
                snapshot.DurationMs,
                snapshot.Assessment.Value,
                snapshot.Assessment.Confidence,
                snapshot.Warnings.Length,
                snapshot.Errors.Length);

    private static PimaxRegistrationSnapshotMetadata Metadata(PimaxUsbEnumerationSnapshot? snapshot)
        => snapshot is null
            ? new PimaxRegistrationSnapshotMetadata(
                PimaxUsbEnumerationSchema.Version,
                DateTimeOffset.MinValue,
                null,
                "unavailable",
                PimaxRegistrationConfidence.Insufficient,
                0,
                1)
            : new PimaxRegistrationSnapshotMetadata(
                snapshot.SchemaVersion,
                snapshot.CollectedAt,
                null,
                $"{snapshot.InventorySummary.TotalDevices} inventory records; {snapshot.CandidateDevices.Length} candidates",
                snapshot.Errors.Length == 0 ? PimaxRegistrationConfidence.Probable : PimaxRegistrationConfidence.Insufficient,
                snapshot.Warnings.Length,
                snapshot.Errors.Length);
}
