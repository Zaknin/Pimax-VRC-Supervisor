using System.Text.Json;
using Xunit;

public sealed class PimaxProcessGroupLaunchRecipeTests
{
    [Fact]
    public void CompleteShortcutCandidateBuildsShellActivationCandidateButNotExecutable()
    {
        var snapshot = PimaxProcessGroupLaunchRecipeModel.Build(
            Input(Candidate()),
            DateTimeOffset.Parse("2026-06-20T00:00:00Z"));

        Assert.Equal(PimaxLaunchRecipeSchema.Version, snapshot.Schema);
        Assert.Equal(PimaxProcessGroupLaunchRecipeState.ReadyForShellActivationValidation, snapshot.Recipe.State);
        Assert.False(snapshot.Recipe.Executable);
        Assert.Equal(PimaxProcessGroupReadinessState.GroupReadyAndRegistered, snapshot.Readiness.State);
        Assert.Equal(@"<pimax>\PimaxClient\pimaxui\PimaxClient.exe", snapshot.SelectedCandidate?.SanitizedPath);
        Assert.Equal("", snapshot.Recipe.Arguments);
        Assert.Equal(@"<pimax>\PimaxClient\pimaxui", snapshot.Recipe.WorkingDirectory);
        Assert.Contains("Direct PimaxClient.exe process creation is rejected", snapshot.HumanReadableSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing", false, true, true, true, "launcher file missing")]
    [InlineData("wrong-root", true, false, true, true, "outside expected Pimax install root")]
    [InlineData("wrong-product", true, true, false, true, "product metadata")]
    [InlineData("signer-mismatch", true, true, true, false, "signer")]
    public void CandidateVerificationRejectsUnsafeEvidence(string source, bool exists, bool root, bool product, bool signer, string expectedBlocker)
    {
        var candidate = Candidate(source: source, exists: exists, root: root, product: product, signer: signer);
        var snapshot = PimaxProcessGroupLaunchRecipeModel.Build(Input(candidate), DateTimeOffset.UtcNow);

        Assert.NotEqual(PimaxProcessGroupLaunchRecipeState.ReadyForShellActivationValidation, snapshot.Recipe.State);
        Assert.Contains(snapshot.LauncherCandidates.Single().Blockers, blocker => blocker.Contains(expectedBlocker, StringComparison.OrdinalIgnoreCase));
        Assert.False(snapshot.Recipe.Executable);
    }

    [Fact]
    public void UnknownArgumentsOrWorkingDirectoryBlocksReadyState()
    {
        var snapshot = PimaxProcessGroupLaunchRecipeModel.Build(
            Input(Candidate(arguments: null, workingDirectory: null)),
            DateTimeOffset.UtcNow);

        Assert.Equal(PimaxProcessGroupLaunchRecipeState.VerifiedReadOnly, snapshot.Recipe.State);
        Assert.Contains("working directory", string.Join("\n", snapshot.Blockers), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(snapshot.LauncherCandidates.Single().Blockers, blocker => blocker.Contains("launch arguments", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(PimaxRegistrationState.RegisteredReady, PimaxRegistrationConfidence.Confirmed, PimaxEvidenceFreshness.Current, PimaxProcessGroupReadinessState.GroupReadyAndRegistered)]
    [InlineData(PimaxRegistrationState.LikelyPoweredOnAwaitingRegistration, PimaxRegistrationConfidence.Probable, PimaxEvidenceFreshness.Current, PimaxProcessGroupReadinessState.GroupReadyAwaitingRegistration)]
    [InlineData(PimaxRegistrationState.Unknown, PimaxRegistrationConfidence.Insufficient, PimaxEvidenceFreshness.Unknown, PimaxProcessGroupReadinessState.GroupCompleteRegistrationUnknown)]
    public void ReadinessStatesSeparateGroupFormationFromRegistration(string registration, string confidence, string freshness, string expected)
    {
        var snapshot = PimaxProcessGroupLaunchRecipeModel.Build(
            Input(Candidate(), registration: registration, confidence: confidence, freshness: freshness),
            DateTimeOffset.UtcNow);

        Assert.Equal(expected, snapshot.Readiness.State);
    }

    [Fact]
    public void MissingRequiredMemberProducesPartialReadinessAndIncompleteRecipe()
    {
        var snapshot = PimaxProcessGroupLaunchRecipeModel.Build(
            Input(Candidate(), requiredMembers: ["PimaxClient", "DeviceSetting"]),
            DateTimeOffset.UtcNow);

        Assert.Equal(PimaxProcessGroupReadinessState.GroupPartial, snapshot.Readiness.State);
        Assert.Equal(PimaxProcessGroupLaunchRecipeState.VerifiedReadOnly, snapshot.Recipe.State);
        Assert.Contains("PiPlayService", snapshot.Readiness.RequiredMembersMissing);
    }

    [Fact]
    public void PrivacyOutputExcludesRawRuntimeIdentifiers()
    {
        var snapshot = PimaxProcessGroupLaunchRecipeModel.Build(Input(Candidate()), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(snapshot, PimaxRepairJson.Options);

        Assert.DoesNotContain("12345", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"USB\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SERIALNUMBER", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticSafetyDoesNotAddPimaxMutationOrNetworkPaths()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxProcessGroupLaunchRecipe.cs"));
        string[] forbidden =
        [
            "Process.Start",
            ".Kill(",
            "ServiceController",
            "Restart-Service",
            "Stop-Service",
            "Start-Service",
            "IOCTL_USB_HUB_CYCLE_PORT",
            "CM_Reenumerate",
            "SetupDiCallClassInstaller",
            "SendInput",
            "mouse_event",
            "keybd_event",
            "HttpClient",
            "WebRequest"
        ];

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PimaxLaunchRecipeEvidenceInput Input(
        PimaxLaunchRecipeCandidateEvidence candidate,
        string[]? requiredMembers = null,
        string registration = PimaxRegistrationState.RegisteredReady,
        string confidence = PimaxRegistrationConfidence.Confirmed,
        string freshness = PimaxEvidenceFreshness.Current)
        => new(
            [candidate],
            requiredMembers ?? ["PimaxClient", "DeviceSetting", "PiPlayService", "pi_server", "PiServiceLauncher", "Tobii VR4PIMAXP3B Platform Runtime"],
            ["PiService", "PVRHome", "pi_overlay"],
            registration,
            confidence,
            freshness,
            PimaxHealthOverallStatus.Healthy,
            "Windows Explorer-rooted official Start Menu Shell activation starts the Pimax runtime group.",
            "confirmed",
            [],
            []);

    private static PimaxLaunchRecipeCandidateEvidence Candidate(
        string source = "startMenuShortcut",
        bool exists = true,
        bool root = true,
        bool product = true,
        bool signer = true,
        string? arguments = "",
        string? workingDirectory = @"C:\Program Files\Pimax\PimaxClient\pimaxui")
        => new(
            source,
            root ? PimaxProcessGroupLaunchRecipeModel.CandidateLauncherPath : @"D:\Tools\PimaxClient.exe",
            exists,
            root,
            product,
            signer,
            signer ? "Pimax publisher metadata present." : "unsigned duplicate",
            product ? "PimaxClient" : "OtherClient",
            product ? "Pimax" : "Other",
            "1.43.9.272",
            "1.43.9.272",
            "x64",
            "WindowsGui",
            "asInvoker",
            "exact hash captured privately",
            arguments,
            workingDirectory,
            "PimaxPlay version 1.43.9.272",
            "1.43.9.272",
            "Pimax Technology (Shanghai) Co., Ltd.");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
