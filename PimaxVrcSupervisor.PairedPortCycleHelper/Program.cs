var request = PimaxUsbPairedExperimentRequest.Parse(args);
if (!string.Equals(request.Mode, PimaxUsbPairedExperimentMode.ExecuteElevatedHelper, StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = 2;
    return;
}

var result = await PimaxUsbPairedElevatedExecutor.ExecuteAsync(request, CancellationToken.None);
Environment.ExitCode = result.Success ? 0 : 1;
