using System.Globalization;

internal sealed record TerminalUiLaunchSpec(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

internal static class TerminalUiLaunchArguments
{
    public static TerminalUiLaunchSpec BuildSupervisorOwned(
        string supervisorPath,
        string? configPath,
        int supervisorPid)
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

        arguments.Add("--exit-when-supervisor-exits");
        arguments.Add("--supervisor-pid");
        arguments.Add(supervisorPid.ToString(CultureInfo.InvariantCulture));
        return new TerminalUiLaunchSpec(tuiPath, supervisorDirectory, arguments);
    }
}
