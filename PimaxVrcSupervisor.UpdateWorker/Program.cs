using PimaxVrcSupervisor.Updates;

var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
StandaloneUpdateCheckResultV1 result;
if (!StandaloneUpdateCheckCommand.HasExactArguments(commandLineArgs))
{
    result = StandaloneUpdateCheckCommand.InvalidArguments();
}
else
{
    try
    {
        result = await StandaloneUpdateCheckWorker.RunProductionAsync(
            CancellationToken.None,
            "PimaxVrcSupervisor.UpdateWorker.runtimeconfig.json");
    }
    catch
    {
        result = StandaloneUpdateCheckCommand.Failed(
            "worker_failed",
            "The standalone update check failed before producing a verified result.");
    }
}

Console.WriteLine(StandaloneUpdateCheckJson.Serialize(result));
Environment.ExitCode = result.Success ? 0 : 1;
