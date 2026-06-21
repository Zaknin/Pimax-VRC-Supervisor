using System.Diagnostics;
using System.Text.Json;

namespace PimaxShellValidationHarness;

internal static class Program
{
    private static int _shellRequestCount;

    public static async Task<int> Main(string[] args)
    {
        var parsed = Parse(args);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(parsed.TimeoutSeconds + 45, 60)));

        if (parsed.ObserverChild)
        {
            var refusal = ExecutionGuards.ValidateObserverContext(ExecutionGuards.IsElevated());
            if (refusal is not null)
            {
                Console.Error.WriteLine($"{Classification.E}: {refusal.Message}");
                return 2;
            }

            if (parsed.CorrelationId is null || string.IsNullOrWhiteSpace(parsed.Output))
            {
                Console.Error.WriteLine($"{Classification.E}: observer child requires correlation id and output directory.");
                return 2;
            }

            return await Observer.RunAsync(parsed.CorrelationId.Value, parsed.Output, parsed.TimeoutSeconds, cts.Token);
        }

        return await RunParentAsync(parsed, cts.Token);
    }

    private static async Task<int> RunParentAsync(HarnessArguments args, CancellationToken cancellationToken)
    {
        var processInventory = new SystemProcessInventory();
        var initialProcesses = processInventory.Collect();
        var contextRefusal = ExecutionGuards.ValidateMainContext(
            ExecutionGuards.IsElevated(),
            Environment.UserInteractive,
            Process.GetCurrentProcess().SessionId,
            initialProcesses,
            args.Confirm);
        if (contextRefusal is not null)
        {
            return await RefuseBeforeOutputAsync(contextRefusal);
        }

        var shortcutDiscovery = new ShortcutDiscovery(new ComShortcutReader());
        var shortcut = shortcutDiscovery.Discover();
        if (!shortcut.Accepted || shortcut.Shortcut is null)
        {
            return await RefuseBeforeOutputAsync(new HarnessRefusal("shortcut-refused", string.Join("; ", shortcut.Errors)));
        }

        var launchGroupRefusal = ExecutionGuards.ValidateStoppedLaunchGroup(initialProcesses);
        if (launchGroupRefusal is not null)
        {
            return await RefuseBeforeOutputAsync(launchGroupRefusal);
        }

        var correlationId = Guid.NewGuid();
        var output = ArtifactWriter.CreateResultDirectory(correlationId);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "build.json"), new
        {
            correlationId,
            buildCommit = ArtifactWriter.BuildCommit(),
            executableHash = await ArtifactWriter.ExecutableHashAsync(cancellationToken),
            processPath = Environment.ProcessPath,
            startedAt = DateTimeOffset.Now
        }, cancellationToken);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "initial-process-inventory.json"), initialProcesses, cancellationToken);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "shortcut.json"), new
        {
            accepted = shortcut.Accepted,
            shortcut = shortcut.Shortcut with { Path = ShortcutDiscovery.SanitizePath(shortcut.Shortcut.Path) },
            errors = shortcut.Errors,
            candidates = shortcut.Candidates.Select(candidate => candidate with { Path = ShortcutDiscovery.SanitizePath(candidate.Path) }).ToArray()
        }, cancellationToken);

        var healthProbe = new HealthProbe(processInventory);
        var before = healthProbe.Collect(shellRequestedAt: null);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "health-before.json"), before, cancellationToken);

        if (args.DryRun)
        {
            var dryRun = Classification.Precondition("Dry run requested; observer and Shell activation intentionally skipped.");
            await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "final-result.json"), dryRun, cancellationToken);
            await ArtifactWriter.WriteManifestAsync(output, cancellationToken);
            Console.WriteLine($"{dryRun.Classification}: {dryRun.Reasons[0]}");
            Console.WriteLine(output);
            return 2;
        }

        var observerProcess = StartElevatedObserver(correlationId, output, args.TimeoutSeconds);
        var observerReady = await WaitForObserverReadyAsync(output, TimeSpan.FromSeconds(15), cancellationToken);
        if (!observerReady)
        {
            var refusal = Classification.Precondition("Observer did not become ready before the bounded activation gate.");
            await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "final-result.json"), refusal, cancellationToken);
            await ArtifactWriter.WriteManifestAsync(output, cancellationToken);
            Console.WriteLine($"{refusal.Classification}: {refusal.Reasons[0]}");
            return 2;
        }

        var stateChangedBeforeActivation = ExecutionGuards.ValidateStoppedLaunchGroup(processInventory.Collect()) is not null;
        if (stateChangedBeforeActivation)
        {
            var ambiguous = Classification.Precondition("Launch-owned Pimax state changed before activation.", _shellRequestCount);
            await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "final-result.json"), ambiguous, cancellationToken);
            await ArtifactWriter.WriteManifestAsync(output, cancellationToken);
            Console.WriteLine($"{ambiguous.Classification}: {ambiguous.Reasons[0]}");
            return 2;
        }

        var shellResult = ShellOpenOnce(shortcut.Shortcut);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "shell-request.json"), shellResult, cancellationToken);

        var timeline = await CollectTimelineAsync(healthProbe, shellResult.RequestedAt, args.TimeoutSeconds, cancellationToken);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "health-timeline.json"), timeline, cancellationToken);

        if (observerProcess is not null && !observerProcess.HasExited)
        {
            await observerProcess.WaitForExitAsync(cancellationToken);
        }

        var observer = await TryReadObserverResultAsync(output, cancellationToken);
        var final = Classification.Classify(shellResult, timeline, observer, stateChangedBeforeActivation: false);
        await ArtifactWriter.WriteJsonAsync(Path.Combine(output, "final-result.json"), final, cancellationToken);
        await ArtifactWriter.WriteManifestAsync(output, cancellationToken);
        Console.WriteLine($"{final.Classification}: {final.Meaning}");
        Console.WriteLine(output);
        return final.Classification.StartsWith("A ", StringComparison.Ordinal) ? 0 : 1;
    }

    private static async Task<int> RefuseBeforeOutputAsync(HarnessRefusal refusal)
    {
        Console.Error.WriteLine($"{Classification.E}: {refusal.Message}");
        await Task.CompletedTask;
        return 2;
    }

    private static Process? StartElevatedObserver(Guid correlationId, string output, int timeoutSeconds)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Could not resolve harness executable path.");
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--observer-child --correlation-id {correlationId} --output \"{output}\" --timeout-seconds {timeoutSeconds}",
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        return Process.Start(startInfo);
    }

    private static ShellRequestResult ShellOpenOnce(ShortcutCandidate shortcut)
    {
        var requestedAt = DateTimeOffset.Now;
        try
        {
            if (Interlocked.Exchange(ref _shellRequestCount, 1) != 0)
            {
                throw new InvalidOperationException("Shell one-shot guard rejected a second activation request.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = shortcut.Path,
                Verb = "open",
                UseShellExecute = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)
                    ? Path.GetDirectoryName(shortcut.Path) ?? AppContext.BaseDirectory
                    : shortcut.WorkingDirectory
            })?.Dispose();
            return new ShellRequestResult(requestedAt, ShortcutDiscovery.SanitizePath(shortcut.Path), true, null, null, _shellRequestCount, 0);
        }
        catch (Exception ex)
        {
            return new ShellRequestResult(requestedAt, ShortcutDiscovery.SanitizePath(shortcut.Path), false, ex.GetType().Name, ex.Message, _shellRequestCount, 0);
        }
    }

    private static async Task<HealthSnapshot[]> CollectTimelineAsync(
        HealthProbe healthProbe,
        DateTimeOffset shellRequestedAt,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<HealthSnapshot>();
        var checkpoints = new Queue<int>([5, 15, 30, 60, timeoutSeconds]);
        var started = Stopwatch.StartNew();
        while (started.Elapsed.TotalSeconds <= timeoutSeconds)
        {
            var snapshot = healthProbe.Collect(shellRequestedAt);
            snapshots.Add(snapshot);
            if (snapshots.Count >= 2 && snapshots[^1].SoftwareStackReady && snapshots[^1].RegistrationHealthy && snapshots[^2].SoftwareStackReady && snapshots[^2].RegistrationHealthy)
            {
                break;
            }

            while (checkpoints.Count > 0 && checkpoints.Peek() <= started.Elapsed.TotalSeconds)
            {
                checkpoints.Dequeue();
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return snapshots.ToArray();
    }

    private static async Task<bool> WaitForObserverReadyAsync(string output, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var marker = Path.Combine(output, "observer-ready.json");
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(marker))
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private static async Task<ObserverResult?> TryReadObserverResultAsync(string output, CancellationToken cancellationToken)
    {
        var path = Path.Combine(output, "observer-result.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<ObserverResult>(json, HarnessConstants.JsonOptions);
    }

    internal static HarnessArguments Parse(string[] args)
    {
        var observerChild = false;
        var dryRun = false;
        string? confirm = null;
        Guid? correlationId = null;
        string? output = null;
        var timeoutSeconds = 90;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--observer-child":
                    observerChild = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--confirm" when i + 1 < args.Length:
                    confirm = args[++i];
                    break;
                case "--correlation-id" when i + 1 < args.Length && Guid.TryParse(args[++i], out var parsedGuid):
                    correlationId = parsedGuid;
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--timeout-seconds" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedTimeout):
                    timeoutSeconds = Math.Clamp(parsedTimeout, 5, 300);
                    break;
            }
        }

        return new HarnessArguments(observerChild, dryRun, confirm, correlationId, output, timeoutSeconds);
    }
}
