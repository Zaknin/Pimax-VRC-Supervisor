using System.Globalization;

internal sealed record TerminalUiLaunchSpec(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

internal static class TerminalUiLaunchArguments
{
    public static TerminalUiLaunchSpec BuildPersistentClient(string supervisorPath, string? configPath)
    {
        var spec = Build(supervisorPath, configPath);
        return new TerminalUiLaunchSpec(spec.ExecutablePath, spec.WorkingDirectory, spec.Arguments);
    }

    public static TerminalUiLaunchSpec BuildSupervisorOwned(
        string supervisorPath,
        string? configPath,
        int supervisorPid)
    {
        var spec = Build(supervisorPath, configPath);
        var arguments = spec.Arguments.ToList();
        arguments.Add("--exit-when-supervisor-exits");
        arguments.Add("--supervisor-pid");
        arguments.Add(supervisorPid.ToString(CultureInfo.InvariantCulture));
        return new TerminalUiLaunchSpec(spec.ExecutablePath, spec.WorkingDirectory, arguments);
    }

    private static TerminalUiLaunchSpec Build(string supervisorPath, string? configPath)
    {
        var supervisorDirectory = string.IsNullOrWhiteSpace(supervisorPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(supervisorPath) ?? AppContext.BaseDirectory;
        var tuiPath = Path.Combine(supervisorDirectory, "PimaxVrcSupervisorTui.exe");
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            arguments.Add("--config");
            arguments.Add(configPath);
        }

        return new TerminalUiLaunchSpec(tuiPath, supervisorDirectory, arguments);
    }
}
