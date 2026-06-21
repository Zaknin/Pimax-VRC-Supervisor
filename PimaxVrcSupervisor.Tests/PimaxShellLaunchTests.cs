using Xunit;

public sealed class PimaxShellLaunchTests
{
    [Fact]
    public void KnownCommonStartMenuShortcutIsAccepted()
    {
        using var root = new TempDirectory();
        var shortcut = Path.Combine(root.Path, "PimaxPlay.lnk");
        File.WriteAllText(shortcut, "");
        var discovery = new PimaxShellShortcutDiscovery(
            new FakeShortcutReader(Trusted(shortcut, root.Path)),
            [root.Path]);

        var result = discovery.Discover();

        Assert.True(result.Accepted);
        Assert.Equal("trusted", result.State);
    }

    [Fact]
    public void CurrentUserStartMenuShortcutIsAccepted()
    {
        using var root = new TempDirectory();
        var programs = Path.Combine(root.Path, "Programs");
        Directory.CreateDirectory(programs);
        var shortcut = Path.Combine(programs, "PimaxPlay.lnk");
        File.WriteAllText(shortcut, "");
        var discovery = new PimaxShellShortcutDiscovery(
            new FakeShortcutReader(Trusted(shortcut, programs)),
            [programs]);

        var result = discovery.Discover();

        Assert.True(result.Accepted);
    }

    [Fact]
    public void MissingShortcutIsRefused()
    {
        using var root = new TempDirectory();
        var result = new PimaxShellShortcutDiscovery(new FakeShortcutReader(), [root.Path]).Discover();

        Assert.False(result.Accepted);
        Assert.Equal("missing", result.State);
    }

    [Fact]
    public void DuplicateTrustedShortcutIsRefused()
    {
        using var root = new TempDirectory();
        var sub = Path.Combine(root.Path, "Pimax");
        Directory.CreateDirectory(sub);
        var first = Path.Combine(root.Path, "PimaxPlay.lnk");
        var second = Path.Combine(sub, "PimaxPlay.lnk");
        File.WriteAllText(first, "");
        File.WriteAllText(second, "");
        var reader = new FakeShortcutReader(
            Trusted(first, root.Path),
            Trusted(second, root.Path));

        var result = new PimaxShellShortcutDiscovery(reader, [root.Path]).Discover();

        Assert.False(result.Accepted);
        Assert.Equal("duplicate", result.State);
    }

    [Theory]
    [InlineData(@"https://example.invalid/PimaxPlay.lnk", @"C:\Program Files\Pimax\PimaxClient\PimaxClient.exe", "")]
    [InlineData(@"C:\Start\PimaxPlay.lnk", @"\\server\share\PimaxClient.exe", "")]
    [InlineData(@"C:\Start\PimaxPlay.lnk", @"C:\Program Files\Pimax\repair.ps1", "")]
    [InlineData(@"C:\Start\PimaxPlay.lnk", @"C:\Program Files\Pimax\repair.cmd", "")]
    [InlineData(@"C:\Start\PimaxPlay.lnk", @"C:\Windows\System32\notepad.exe", "")]
    [InlineData(@"C:\Start\PimaxPlay.lnk", @"C:\Program Files\Pimax\PimaxClient\PimaxClient.exe", "--unexpected")]
    public void UntrustedShortcutShapesAreRefused(string path, string target, string arguments)
    {
        var candidate = new PimaxShellLaunchShortcut(path, target, arguments, "", @"C:\Start");

        Assert.False(PimaxShellShortcutDiscovery.IsTrusted(candidate));
    }

    [Theory]
    [InlineData("PimaxClient")]
    [InlineData("DeviceSetting")]
    [InlineData("PiPlayService")]
    [InlineData("PiService")]
    [InlineData("pi_server")]
    [InlineData("PVRHome")]
    [InlineData("pi_overlay")]
    [InlineData("lighthouse_console")]
    public void LaunchOwnedProcessBlocks(string name)
    {
        var blocked = PimaxShellLaunchRunner.FindBlockingProcesses([new(name, 10, 1)]);

        Assert.Equal([name], blocked);
    }

    [Theory]
    [InlineData("PiPlatformService_64")]
    [InlineData("platform_runtime_VR4PIMAXP3B_service")]
    [InlineData("PiServiceLauncher")]
    [InlineData("vrss_gaze_provider")]
    public void PermittedPersistentProcessDoesNotBlock(string name)
    {
        var blocked = PimaxShellLaunchRunner.FindBlockingProcesses([new(name, 10, 0)]);

        Assert.Empty(blocked);
    }

    [Fact]
    public async Task NormalInteractiveContextCanLaunchAndRegister()
    {
        using var root = ShortcutRoot(out var shortcut);
        var launcher = new FakeLauncher();
        var runner = Runner(
            root.Path,
            Trusted(shortcut, root.Path),
            launcher,
            new FakeVerifier(
                Sample(software: true, headset: true, registration: true),
                Sample(software: true, headset: true, registration: true)));

        var result = await runner.RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.LaunchedAndRegistered, result.Result);
        Assert.True(result.Success);
        Assert.True(result.ShellRequestAccepted);
        Assert.Equal(1, result.ShellRequestCount);
        Assert.Equal(0, result.RetryCount);
        Assert.Equal(2, result.StableHealthySampleCount);
    }

    [Theory]
    [InlineData(true, true, 1, "non-elevated")]
    [InlineData(false, false, 1, "interactive")]
    [InlineData(false, true, 0, "session 0")]
    public async Task InvalidExecutionContextIsRefused(bool elevated, bool interactive, int sessionId, string expected)
    {
        using var root = ShortcutRoot(out var shortcut);
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                new FakeLauncher(),
                new FakeVerifier(),
                elevated: elevated,
                interactive: interactive,
                sessionId: sessionId)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.PreconditionRefused, result.Result);
        Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, result.ShellRequestCount);
    }

    [Fact]
    public async Task ExplorerSessionMismatchIsRefused()
    {
        using var root = ShortcutRoot(out var shortcut);
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                new FakeLauncher(),
                new FakeVerifier(),
                processes: [new("explorer", 1, 2)])
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.PreconditionRefused, result.Result);
        Assert.Contains(result.Errors, error => error.Contains("explorer.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WatcherContextIsRefused()
    {
        using var root = ShortcutRoot(out var shortcut);
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                new FakeLauncher(),
                new FakeVerifier(),
                commandLineArgs: ["pimax-shell-launch-json", "--watch-vrchat-auto-launch"])
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.PreconditionRefused, result.Result);
        Assert.Contains(result.Errors, error => error.Contains("watcher", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunningPimaxPlayRefusesBeforeShellRequest()
    {
        using var root = ShortcutRoot(out var shortcut);
        var launcher = new FakeLauncher();
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                launcher,
                new FakeVerifier(),
                processes: [Explorer(), new("PimaxClient", 2, 1)])
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.PreconditionRefused, result.Result);
        Assert.Equal("blocked", result.PreLaunchState);
        Assert.Equal(0, launcher.Calls);
        Assert.Equal(0, result.ShellRequestCount);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    public async Task FailedShellRequestIsClassified()
    {
        using var root = ShortcutRoot(out var shortcut);
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                new FakeLauncher(accepted: false),
                new FakeVerifier())
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.ShellLaunchFailed, result.Result);
        Assert.Equal(1, result.ShellRequestCount);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    public async Task LaunchedButNotRegisteredIsClassified()
    {
        using var root = ShortcutRoot(out var shortcut);
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                new FakeLauncher(),
                new FakeVerifier(Sample(software: true, headset: true, registration: false)))
            .RunAsync(Request(timeoutSeconds: 0), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.LaunchedButNotRegistered, result.Result);
        Assert.False(result.RegistrationHealthy);
    }

    [Fact]
    public async Task VerificationInconclusiveIsClassified()
    {
        using var root = ShortcutRoot(out var shortcut);
        var result = await Runner(
                root.Path,
                Trusted(shortcut, root.Path),
                new FakeLauncher(),
                new FakeVerifier(Sample(software: false, headset: false, registration: false)))
            .RunAsync(Request(timeoutSeconds: 0), CancellationToken.None);

        Assert.Equal(PimaxShellLaunchResultName.VerificationInconclusive, result.Result);
    }

    [Fact]
    public void OneShotLauncherRefusesSecondRequestAndNeverRetries()
    {
        var launcher = new FakeLauncher();
        var shortcut = Trusted(@"C:\Start\PimaxPlay.lnk", @"C:\Start");

        var first = launcher.OpenOnce(shortcut);
        var second = launcher.OpenOnce(shortcut);

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Equal(1, first.ShellRequestCount);
        Assert.Equal(1, second.ShellRequestCount);
        Assert.Equal(0, first.RetryCount);
        Assert.Equal(0, second.RetryCount);
    }

    [Fact]
    public void ProductionCommandDispatchesBeforeNormalStartupPaths()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "Program.cs"));
        var command = text.IndexOf("pimax-shell-launch-json", StringComparison.Ordinal);

        Assert.True(command >= 0);
        Assert.True(command < text.IndexOf("if (startupContext.ShouldHideConsole)", StringComparison.Ordinal));
        Assert.True(command < text.IndexOf("DirectLaunchMigration.RunAsync", StringComparison.Ordinal));
        Assert.True(command < text.IndexOf("AutoLaunchWatcher.RunAsync", StringComparison.Ordinal));
        Assert.True(command < text.IndexOf("StartupIntegration.ApplyAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void FeatureLocalStaticSafetyScanFindsOnlyOneShellOpenAndNoMutationSurface()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "PimaxVrcSupervisor", "PimaxShellLaunch.cs"));

        Assert.Equal(1, Count(text, "Process.Start("));
        Assert.Contains("UseShellExecute = true", text);
        Assert.Contains("Verb = \"open\"", text);
        foreach (var forbidden in new[]
        {
            "schtasks",
            "Register-ScheduledTask",
            "Set-ScheduledTask",
            "New-ScheduledTask",
            "Stop-Process",
            "Process.Kill",
            "taskkill",
            "ServiceController.Stop",
            "ServiceController.Start",
            "pnputil",
            "devcon",
            "USB reset",
            "DisplayPort reset",
            "PimaxClient.exe"
        })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PimaxShellLaunchRunner Runner(
        string root,
        PimaxShellLaunchShortcut shortcut,
        IPimaxShellLauncher launcher,
        IPimaxShellLaunchVerifier verifier,
        bool elevated = false,
        bool interactive = true,
        int sessionId = 1,
        PimaxShellLaunchProcessRecord[]? processes = null,
        string[]? commandLineArgs = null)
        => new(
            new FakeInventory(processes ?? [Explorer()]),
            new PimaxShellShortcutDiscovery(new FakeShortcutReader(shortcut), [root]),
            launcher,
            verifier,
            isWindows: () => true,
            isElevated: () => elevated,
            isUserInteractive: () => interactive,
            currentSessionId: () => sessionId,
            delayAsync: (_, _) => Task.CompletedTask,
            commandLineArgs: commandLineArgs ?? ["pimax-shell-launch-json"]);

    private static TempDirectory ShortcutRoot(out string shortcut)
    {
        var root = new TempDirectory();
        shortcut = Path.Combine(root.Path, "PimaxPlay.lnk");
        File.WriteAllText(shortcut, "");
        return root;
    }

    private static PimaxShellLaunchRequest Request(int timeoutSeconds = 5)
        => new(TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMilliseconds(1));

    private static PimaxShellLaunchProcessRecord Explorer()
        => new("explorer", 1, 1);

    private static PimaxShellLaunchShortcut Trusted(string path, string root)
        => new(path, @"C:\Program Files\Pimax\PimaxClient\PimaxClient.exe", "", @"C:\Program Files\Pimax\PimaxClient", root);

    private static PimaxShellLaunchVerificationSample Sample(bool software, bool headset, bool registration, string[]? errors = null)
        => new(DateTimeOffset.Now, software, headset, registration, registration ? PimaxRegistrationState.RegisteredReady : PimaxRegistrationState.Unknown, registration ? PimaxRegistrationConfidence.Confirmed : PimaxRegistrationConfidence.Insufficient, [], errors ?? []);

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PimaxVrcSupervisor.sln"))
                || Directory.Exists(Path.Combine(directory, "PimaxVrcSupervisor")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class FakeInventory(PimaxShellLaunchProcessRecord[] processes) : IPimaxShellLaunchProcessInventory
    {
        public PimaxShellLaunchProcessRecord[] Collect() => processes;
    }

    private sealed class FakeShortcutReader(params PimaxShellLaunchShortcut[] shortcuts) : IPimaxShellShortcutReader
    {
        public PimaxShellLaunchShortcut Read(string path, string sourceRoot)
            => shortcuts.First(shortcut => string.Equals(shortcut.Path, path, StringComparison.OrdinalIgnoreCase)) with { SourceRoot = sourceRoot };
    }

    private sealed class FakeLauncher(bool accepted = true) : IPimaxShellLauncher
    {
        private int _requestCount;
        public int Calls => _requestCount;

        public PimaxShellRequestResult OpenOnce(PimaxShellLaunchShortcut shortcut)
        {
            if (Interlocked.Exchange(ref _requestCount, 1) != 0)
            {
                return new PimaxShellRequestResult(DateTimeOffset.Now, false, "InvalidOperationException", "Shell one-shot guard rejected a second activation request.", _requestCount, 0);
            }

            return accepted
                ? new PimaxShellRequestResult(DateTimeOffset.Now, true, null, null, _requestCount, 0)
                : new PimaxShellRequestResult(DateTimeOffset.Now, false, "IOException", "boom", _requestCount, 0);
        }
    }

    private sealed class FakeVerifier(params PimaxShellLaunchVerificationSample[] samples) : IPimaxShellLaunchVerifier
    {
        private int _index;

        public Task<PimaxShellLaunchVerificationSample> CollectAsync(CancellationToken cancellationToken)
        {
            var sample = samples.Length == 0
                ? Sample(software: false, headset: false, registration: false)
                : samples[Math.Min(_index, samples.Length - 1)];
            _index++;
            return Task.FromResult(sample);
        }
    }
}
