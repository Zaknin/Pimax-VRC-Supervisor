using System.Diagnostics;
using System.Text.Json;

internal static class PimaxConnectivityJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal sealed class PimaxConnectivitySnapshotCollector
{
    private static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IPimaxProcessRunner _processRunner;

    public PimaxConnectivitySnapshotCollector()
        : this(new PimaxProcessRunner())
    {
    }

    internal PimaxConnectivitySnapshotCollector(IPimaxProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<PimaxConnectivitySnapshot> CollectAsync(
        SupervisorConfig config,
        CancellationToken cancellationToken)
    {
        var collectedAt = DateTimeOffset.Now;
        var startedAt = Stopwatch.GetTimestamp();
        using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallTimeout.CancelAfter(DefaultOverallTimeout);
        var token = overallTimeout.Token;
        var probeTimeout = TimeSpan.FromSeconds(Math.Max(DefaultProbeTimeout.TotalSeconds, Math.Min(10, config.DeviceProbeTimeoutSeconds)));

        PimaxInstallationObservation installation;
        PimaxProcessObservation processes;
        PimaxServiceObservation services;
        PimaxDeviceObservation devices;
        PimaxRuntimeEvidenceObservation runtimeEvidence;
        PimaxSteamVrDriverObservation steamVrDriver;

        try
        {
            installation = PimaxInstallationProbe.Collect();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            installation = new PimaxInstallationObservation(PimaxProbeStatus.Error, [], [], [], [ex.Message]);
        }

        try
        {
            processes = PimaxProcessProbe.Collect(installation);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            processes = new PimaxProcessObservation(PimaxProbeStatus.Error, [], [], [ex.Message]);
        }

        try
        {
            services = await PimaxServiceProbe.CollectAsync(_processRunner, probeTimeout, token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            services = new PimaxServiceObservation(PimaxProbeStatus.Error, [], [], [ex.Message]);
        }

        try
        {
            devices = await PimaxDeviceProbe.CollectAsync(_processRunner, probeTimeout, token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            devices = new PimaxDeviceObservation(PimaxProbeStatus.Error, [], [], false, false, false, [], [], ["Windows device probe timed out."]);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            devices = new PimaxDeviceObservation(PimaxProbeStatus.Error, [], [], false, false, false, [], [], [ex.Message]);
        }

        try
        {
            runtimeEvidence = PimaxRuntimeEvidenceProbe.Collect(config, collectedAt);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            runtimeEvidence = new PimaxRuntimeEvidenceObservation(
                PimaxProbeStatus.Error,
                collectedAt,
                0,
                [],
                null,
                null,
                [],
                [ex.Message]);
        }

        try
        {
            steamVrDriver = PimaxSteamVrDriverProbe.Collect();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            steamVrDriver = new PimaxSteamVrDriverObservation(PimaxProbeStatus.Error, [], false, [], [ex.Message]);
        }

        var assessment = PimaxConnectivityAssessment.Evaluate(
            installation,
            processes,
            services,
            devices,
            runtimeEvidence,
            steamVrDriver);
        var warnings = Merge(
            installation.Warnings,
            processes.Warnings,
            services.Warnings,
            devices.Warnings,
            runtimeEvidence.Warnings,
            steamVrDriver.Warnings,
            assessment.Warnings);
        var errors = Merge(
            installation.Errors,
            processes.Errors,
            services.Errors,
            devices.Errors,
            runtimeEvidence.Errors,
            steamVrDriver.Errors);

        return new PimaxConnectivitySnapshot(
            PimaxConnectivitySchema.Version,
            collectedAt,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            installation,
            processes,
            services,
            devices,
            runtimeEvidence,
            steamVrDriver,
            assessment,
            assessment.Confidence,
            warnings,
            errors);
    }

    private static string[] Merge(params string[][] values)
        => values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
