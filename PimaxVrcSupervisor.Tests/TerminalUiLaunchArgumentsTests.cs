using Xunit;

public sealed class TerminalUiLaunchArgumentsTests
{
    [Fact]
    public void SupervisorOwnedLaunchIncludesConfigExitFlagAndSupervisorPid()
    {
        var spec = TerminalUiLaunchArguments.BuildSupervisorOwned(
            @"D:\VR\PimaxVrcSupervisor.exe",
            @"D:\VR\supervisor.config.json",
            1234);

        Assert.Equal(@"D:\VR\PimaxVrcSupervisorTui.exe", spec.ExecutablePath);
        Assert.Equal(@"D:\VR", spec.WorkingDirectory);
        Assert.Equal(
            ["--config", @"D:\VR\supervisor.config.json", "--exit-when-supervisor-exits", "--supervisor-pid", "1234"],
            spec.Arguments);
    }

    [Fact]
    public void SupervisorOwnedLaunchOmitsConfigWhenNoConfigPathExists()
    {
        var spec = TerminalUiLaunchArguments.BuildSupervisorOwned(
            @"D:\VR\PimaxVrcSupervisor.exe",
            null,
            1234);

        Assert.Equal(["--exit-when-supervisor-exits", "--supervisor-pid", "1234"], spec.Arguments);
    }

    [Fact]
    public void SupervisorOwnedLaunchDoesNotDuplicateOwnershipFlags()
    {
        var spec = TerminalUiLaunchArguments.BuildSupervisorOwned(
            @"D:\VR\PimaxVrcSupervisor.exe",
            @"D:\VR\supervisor.config.json",
            1234);

        Assert.Single(spec.Arguments, argument => argument == "--exit-when-supervisor-exits");
        Assert.Single(spec.Arguments, argument => argument == "--supervisor-pid");
    }
}
