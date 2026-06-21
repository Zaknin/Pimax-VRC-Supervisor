using PimaxShellValidationHarness;
using Xunit;

public sealed class HarnessTests
{
    [Fact]
    public void Main_mode_refuses_elevation()
    {
        var refusal = ExecutionGuards.ValidateMainContext(true, true, 1, Explorer(), HarnessConstants.ConfirmationPhrase);
        Assert.Equal("main-mode-elevated", refusal?.Code);
    }

    [Fact]
    public void Observer_mode_requires_elevation()
    {
        Assert.Equal("observer-not-elevated", ExecutionGuards.ValidateObserverContext(false)?.Code);
        Assert.Null(ExecutionGuards.ValidateObserverContext(true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public void Missing_or_wrong_confirmation_refuses(string? confirmation)
    {
        var refusal = ExecutionGuards.ValidateMainContext(false, true, 1, Explorer(), confirmation);
        Assert.Equal("missing-or-wrong-confirmation", refusal?.Code);
    }

    [Fact]
    public void Missing_or_duplicate_shortcut_refuses()
    {
        var missing = new ShortcutDiscovery(new FakeReader([]), [Path.GetTempPath()]).Discover();
        Assert.False(missing.Accepted);

        using var root = new TempRoot();
        File.WriteAllText(Path.Combine(root.Path, "PimaxPlay.lnk"), "");
        var sub = Directory.CreateDirectory(Path.Combine(root.Path, "Nested")).FullName;
        File.WriteAllText(Path.Combine(sub, "PimaxPlay.lnk"), "");
        var duplicate = new ShortcutDiscovery(new FakeReader([
            Trusted(Path.Combine(root.Path, "PimaxPlay.lnk")),
            Trusted(Path.Combine(sub, "PimaxPlay.lnk"))
        ]), [root.Path]).Discover();
        Assert.False(duplicate.Accepted);
    }

    [Fact]
    public void Direct_executable_candidate_refuses()
    {
        var candidate = Trusted(@"C:\Temp\PimaxClient.exe");
        Assert.False(ShortcutDiscovery.IsTrusted(candidate));
    }

    [Fact]
    public void Launch_owned_process_blocks_but_persistent_processes_do_not()
    {
        Assert.Equal("launch-owned-process-running", ExecutionGuards.ValidateStoppedLaunchGroup([new("PimaxClient", 10, 1, null, null)])?.Code);
        Assert.Null(ExecutionGuards.ValidateStoppedLaunchGroup([new("PiPlatformService_64", 11, 0, null, null)]));
    }

    [Fact]
    public void One_shot_classification_values_are_stable()
    {
        var shell = new ShellRequestResult(DateTimeOffset.Now, "x", true, null, null, 1, 0);
        var healthy1 = Snapshot(1, software: true, registration: true);
        var healthy2 = Snapshot(2, software: true, registration: true);
        var observer = new ObserverResult(Guid.NewGuid(), DateTimeOffset.Now, DateTimeOffset.Now, true, null, []);
        Assert.Equal(Classification.A, Classification.Classify(shell, [healthy1, healthy2], observer, false).Classification);

        Assert.Equal(Classification.B, Classification.Classify(shell, [Snapshot(1, software: true, registration: false)], observer, false).Classification);
        var failedShell = shell with { Accepted = false };
        Assert.Equal(Classification.C, Classification.Classify(failedShell, [Snapshot(1, software: false, registration: false)], observer, false).Classification);
        Assert.Equal(Classification.D, Classification.Classify(shell, [healthy1], null, false).Classification);
        Assert.Equal(Classification.E, Classification.Precondition("no").Classification);
    }

    [Fact]
    public void Output_json_is_valid()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Classification.Precondition("observer-not-ready"), HarnessConstants.JsonOptions);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(Classification.E, document.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Source_has_no_scheduler_config_watcher_mutation_interfaces()
    {
        var root = FindRepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "tools", "PimaxShellValidationHarness"), "*.cs", SearchOption.AllDirectories);
        var source = string.Join('\n', files.Select(File.ReadAllText));
        Assert.DoesNotContain("ScheduledTask", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supervisor.config", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PimaxVrcSupervisorWatcher", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kill(", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceIoControl", source, StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessRecord[] Explorer()
        => [new("explorer", 1, 1, null, null)];

    private static ShortcutCandidate Trusted(string path)
        => new(path, @"C:\Program Files\Pimax\PimaxClient\PimaxClient.exe", "", @"C:\Program Files\Pimax\PimaxClient", "root");

    private static HealthSnapshot Snapshot(double seconds, bool software, bool registration)
        => new(
            DateTimeOffset.Now,
            seconds,
            software ? [new("PimaxClient", 1, [1])] : [],
            software,
            registration,
            registration,
            registration,
            registration,
            false,
            []);

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class FakeReader : IShortcutReader
    {
        private readonly Dictionary<string, ShortcutCandidate> _candidates;

        public FakeReader(IEnumerable<ShortcutCandidate> candidates)
        {
            _candidates = candidates.ToDictionary(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);
        }

        public ShortcutCandidate Read(string path, string sourceRoot)
            => _candidates[path];
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
            => Directory.Delete(Path, recursive: true);
    }
}
