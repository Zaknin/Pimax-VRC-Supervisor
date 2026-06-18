var request = PimaxUsbPortCycleRequest.Parse(args);
if (!string.Equals(request.Mode, PimaxUsbPortCycleMode.ExecuteElevatedHelper, StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = 2;
    return;
}

var result = await PimaxUsbPortCycleElevatedExecutor.ExecuteAsync(request, CancellationToken.None);
Environment.ExitCode = result.Success ? 0 : 1;
