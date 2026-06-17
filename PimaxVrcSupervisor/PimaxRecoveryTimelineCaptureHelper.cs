using System.Diagnostics;

internal static class PimaxRecoveryTimelineCaptureHelper
{
    public static ProcessStartInfo BuildPowerShellStartInfo(
        string scriptPath,
        string dllPath,
        string outputDirectory,
        string stopFile,
        string expectedHash,
        int intervalSeconds,
        string? standardOutputPath,
        string? standardErrorPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path is required.", nameof(scriptPath));
        }

        if (intervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = !string.IsNullOrWhiteSpace(standardOutputPath),
            RedirectStandardError = !string.IsNullOrWhiteSpace(standardErrorPath)
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-DllPath");
        startInfo.ArgumentList.Add(dllPath);
        startInfo.ArgumentList.Add("-OutputDir");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("-StopFile");
        startInfo.ArgumentList.Add(stopFile);
        startInfo.ArgumentList.Add("-ExpectedHash");
        startInfo.ArgumentList.Add(expectedHash);
        startInfo.ArgumentList.Add("-IntervalSeconds");
        startInfo.ArgumentList.Add(intervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-StandardOutputPath");
        startInfo.ArgumentList.Add(standardOutputPath ?? "");
        startInfo.ArgumentList.Add("-StandardErrorPath");
        startInfo.ArgumentList.Add(standardErrorPath ?? "");
        return startInfo;
    }
}
