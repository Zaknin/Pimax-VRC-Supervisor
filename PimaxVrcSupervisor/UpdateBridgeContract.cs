using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PimaxVrcSupervisor.Updates;

internal enum SupervisorUpdatePolicy
{
    Disabled,
    Notify
}

internal static class SupervisorUpdatePolicyContract
{
    public static SupervisorUpdatePolicy ParseConfigValue(string? value)
        => string.Equals(value, nameof(SupervisorUpdatePolicy.Notify), StringComparison.Ordinal)
            ? SupervisorUpdatePolicy.Notify
            : SupervisorUpdatePolicy.Disabled;
}

internal sealed record UpdateCheckOperationSnapshot(
    string OperationId,
    string Status,
    bool? Success,
    string ResultCode,
    string ResultSummary,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

internal sealed record UpdateStatusSnapshotV1(
    int SchemaVersion,
    string Policy,
    string Channel,
    string CurrentVersion,
    string? LatestVerifiedVersion,
    bool UpdateAvailable,
    bool Dismissed,
    string? DismissedVersion,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessfulCheckAt,
    string? LastErrorCode,
    string? LastErrorSummary,
    bool VerificationConfigured,
    bool AutomaticCheckDue,
    bool CheckInProgress,
    UpdateCheckOperationSnapshot? Operation);

internal sealed record UpdateActionAcceptance(
    bool Accepted,
    bool AlreadyInProgress,
    string? OperationId,
    string Message);

internal interface IConfiguratorUpdateBridge
{
    Task<UpdateStatusSnapshotV1> QueryStatusAsync(CancellationToken cancellationToken);

    Task<UpdateActionAcceptance> StartCheckAsync(CancellationToken cancellationToken);

    Task<UpdateActionAcceptance> DismissAsync(CancellationToken cancellationToken);

    Task<UpdateActionAcceptance> ClearDismissalAsync(CancellationToken cancellationToken);
}

internal sealed record ConfiguratorUpdateCheckResult(
    UpdateActionAcceptance Acceptance,
    UpdateStatusSnapshotV1? TerminalStatus,
    string Summary);

internal sealed class ConfiguratorUpdateCheckRunner
{
    private readonly IConfiguratorUpdateBridge _bridge;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _active;

    public ConfiguratorUpdateCheckRunner(
        IConfiguratorUpdateBridge bridge,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _bridge = bridge;
        _delay = delay ?? Task.Delay;
    }

    public bool IsActive => Volatile.Read(ref _active) == 1;

    public async Task<ConfiguratorUpdateCheckResult> TryRunAsync(
        Action<UpdateStatusSnapshotV1>? statusObserved,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return new ConfiguratorUpdateCheckResult(
                new UpdateActionAcceptance(false, true, null, "An update check is already running in this Configurator."),
                null,
                "An update check is already running in this Configurator.");
        }

        try
        {
            var acceptance = await _bridge.StartCheckAsync(cancellationToken);
            if (!acceptance.Accepted || acceptance.OperationId is null)
            {
                return new ConfiguratorUpdateCheckResult(acceptance, null, acceptance.Message);
            }

            for (var attempt = 0; attempt < 80; attempt++)
            {
                var status = await _bridge.QueryStatusAsync(cancellationToken);
                statusObserved?.Invoke(status);
                if (status.Operation is { } operation
                    && string.Equals(operation.OperationId, acceptance.OperationId, StringComparison.Ordinal)
                    && operation.CompletedAt is not null)
                {
                    return new ConfiguratorUpdateCheckResult(acceptance, status, operation.ResultSummary);
                }

                await _delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            return new ConfiguratorUpdateCheckResult(
                acceptance,
                null,
                "The check is still running. Its terminal result remains available from the Supervisor.");
        }
        finally
        {
            Interlocked.Exchange(ref _active, 0);
        }
    }
}

internal sealed class ConfiguratorUpdateBridgeClient : IConfiguratorUpdateBridge
{
    private const int Port = 37957;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<UpdateStatusSnapshotV1> QueryStatusAsync(CancellationToken cancellationToken)
        => SendQueryAsync(cancellationToken);

    public Task<UpdateActionAcceptance> StartCheckAsync(CancellationToken cancellationToken)
        => SendActionAsync("check-for-updates", cancellationToken);

    public Task<UpdateActionAcceptance> DismissAsync(CancellationToken cancellationToken)
        => SendActionAsync("dismiss-update", cancellationToken);

    public Task<UpdateActionAcceptance> ClearDismissalAsync(CancellationToken cancellationToken)
        => SendActionAsync("clear-update-dismissal", cancellationToken);

    private static async Task<UpdateStatusSnapshotV1> SendQueryAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(new { requestId, resource = "update-status" }, JsonOptions);
        using var document = await SendAsync("query-json " + payload, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            throw new InvalidOperationException(ReadBoundedString(root, "error") ?? "The Supervisor could not return update status.");
        }

        if (!root.TryGetProperty("data", out var data))
        {
            throw new InvalidOperationException("The Supervisor returned no update status data.");
        }

        return data.Deserialize<UpdateStatusSnapshotV1>(JsonOptions)
            ?? throw new InvalidOperationException("The Supervisor returned invalid update status data.");
    }

    private static async Task<UpdateActionAcceptance> SendActionAsync(string command, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(new
        {
            requestId,
            command,
            confirmed = false,
            source = "configurator",
            sourceClientType = "configurator",
            sourceClientInstanceId = "configurator-" + Environment.ProcessId,
            createdAt = DateTimeOffset.UtcNow
        }, JsonOptions);
        using var document = await SendAsync("action-json " + payload, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var message = ReadBoundedString(root, "message") ?? "The Supervisor returned no result.";
        var accepted = root.TryGetProperty("success", out var success) && success.GetBoolean();
        string? operationId = null;
        var alreadyInProgress = false;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            operationId = ReadBoundedString(data, "operationId");
            alreadyInProgress = data.TryGetProperty("alreadyInProgress", out var duplicate) && duplicate.ValueKind == JsonValueKind.True;
        }

        return new UpdateActionAcceptance(accepted, alreadyInProgress, operationId, message);
    }

    private static async Task<JsonDocument> SendAsync(string command, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", Port, timeout.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
        await writer.WriteLineAsync(command.AsMemory(), timeout.Token).ConfigureAwait(false);
        var response = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Supervisor closed the update bridge without a response.");
        if (Encoding.UTF8.GetByteCount(response) > 128 * 1024)
        {
            throw new InvalidOperationException("The Supervisor update response exceeded its size limit.");
        }

        return JsonDocument.Parse(response);
    }

    private static string? ReadBoundedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return text is { Length: <= 512 } ? text : text?[..512];
    }
}
