using System.Management;
using System.Text.Json;

namespace PimaxShellValidationHarness;

internal sealed class Observer
{
    public static async Task<int> RunAsync(Guid correlationId, string output, int timeoutSeconds, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output);
        var startedAt = DateTimeOffset.Now;
        var events = new List<ObserverEvent>();
        string? error = null;
        var readyWritten = false;
        ManagementEventWatcher? startWatcher = null;
        ManagementEventWatcher? stopWatcher = null;

        try
        {
            startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            startWatcher.EventArrived += (_, args) =>
            {
                lock (events)
                {
                    events.Add(ToEvent("start", args.NewEvent));
                }
            };
            stopWatcher.EventArrived += (_, args) =>
            {
                lock (events)
                {
                    events.Add(ToEvent("stop", args.NewEvent));
                }
            };

            startWatcher.Start();
            stopWatcher.Start();
            await ArtifactWriter.WriteJsonAsync(
                Path.Combine(output, "observer-ready.json"),
                new { correlationId, readyAt = DateTimeOffset.Now },
                cancellationToken);
            readyWritten = true;
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            TryStop(startWatcher);
            TryStop(stopWatcher);
            var snapshot = new ObserverResult(
                correlationId,
                startedAt,
                DateTimeOffset.Now,
                readyWritten,
                error,
                events.OrderBy(item => item.Timestamp).ToArray());
            await File.WriteAllTextAsync(
                Path.Combine(output, "observer-result.json"),
                JsonSerializer.Serialize(snapshot, HarnessConstants.JsonOptions),
                cancellationToken);
        }

        return error is null && readyWritten ? 0 : 2;
    }

    private static ObserverEvent ToEvent(string type, ManagementBaseObject value)
        => new(
            DateTimeOffset.Now,
            type,
            Convert.ToString(value["ProcessName"]) ?? "",
            Convert.ToInt32(value["ProcessID"]),
            value.Properties["ParentProcessID"]?.Value is null ? null : Convert.ToInt32(value["ParentProcessID"]));

    private static void TryStop(ManagementEventWatcher? watcher)
    {
        try
        {
            watcher?.Stop();
            watcher?.Dispose();
        }
        catch
        {
        }
    }
}
