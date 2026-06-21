namespace PimaxShellValidationHarness;

internal static class Classification
{
    public const string A = "A - full success";
    public const string B = "B - software launch only";
    public const string C = "C - Shell activation failure";
    public const string D = "D - ambiguous observation";
    public const string E = "E - precondition refusal";

    public static FinalResult Precondition(string reason, int shellRequests = 0)
        => Build(E, "No activation occurred.", false, shellRequests, 0, [reason]);

    public static FinalResult Classify(
        ShellRequestResult shell,
        HealthSnapshot[] timeline,
        ObserverResult? observer,
        bool stateChangedBeforeActivation)
    {
        if (observer is null || !observer.ReadyMarkerWritten || !string.IsNullOrWhiteSpace(observer.Error) || stateChangedBeforeActivation)
        {
            return Build(D, "Do not productize. Review only the experiment evidence.", shell.Accepted, shell.ShellRequestCount, shell.RetryCount, ["Observer evidence was missing, failed, or pre-activation state changed."]);
        }

        var launched = timeline.Any(ProcessChainStarted);
        var healthyStable = StableHealthy(timeline);

        if (shell.Accepted && launched && healthyStable)
        {
            return Build(A, "The Windows Shell launch mechanism is worth productizing.", true, shell.ShellRequestCount, shell.RetryCount, ["Shell request accepted and software plus registration became stable."]);
        }

        if (shell.Accepted && launched)
        {
            return Build(B, "Shell launch alone does not solve the recovery problem. Stop this development route.", true, shell.ShellRequestCount, shell.RetryCount, ["Software stack launched but registration did not become healthy before timeout."]);
        }

        if (!shell.Accepted && !launched)
        {
            return Build(C, "The programmatic Shell mechanism did not reproduce the manual Start Menu behavior.", true, shell.ShellRequestCount, shell.RetryCount, ["Shell request failed and the expected process chain did not start."]);
        }

        return Build(D, "Do not productize. Review only the experiment evidence.", shell.Accepted, shell.ShellRequestCount, shell.RetryCount, ["Evidence was insufficient or conflicting."]);
    }

    internal static bool StableHealthy(HealthSnapshot[] timeline)
        => timeline.Where(snapshot => snapshot.SoftwareStackReady && snapshot.RegistrationHealthy)
            .TakeLast(2)
            .Count() >= 2;

    private static bool ProcessChainStarted(HealthSnapshot snapshot)
        => snapshot.Processes.Any(process =>
            HarnessConstants.LaunchOwnedProcessNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase)
            && process.Count > 0);

    private static FinalResult Build(
        string classification,
        string meaning,
        bool liveActivationPerformed,
        int shellRequestCount,
        int retryCount,
        string[] reasons)
        => new(classification, meaning, liveActivationPerformed, shellRequestCount, retryCount, reasons, DateTimeOffset.Now);
}
