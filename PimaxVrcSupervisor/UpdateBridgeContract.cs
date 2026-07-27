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
    string Message,
    string? ResultCode = null);

internal static class UpdateStatusUserMessage
{
    public const string NetworkFailure = "Couldn't check for updates. Check your connection and try again.";

    public static string ForResult(string? resultCode, UpdateStatusSnapshotV1? status)
    {
        if (!string.IsNullOrWhiteSpace(resultCode))
        {
            return ForCode(resultCode, status);
        }

        return status is null
            ? NetworkFailure
            : ForStatus(status);
    }

    public static string ForStatus(UpdateStatusSnapshotV1 status)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (!status.VerificationConfigured)
        {
            return "Update verification is not available in this build.";
        }

        if (!string.IsNullOrWhiteSpace(status.LastErrorCode))
        {
            return ForCode(status.LastErrorCode, status);
        }

        if (status.CheckInProgress)
        {
            return "An update check is already in progress.";
        }

        if (status.LastAttemptAt is null)
        {
            return "Updates have not been checked yet.";
        }

        if (string.Equals(status.Operation?.ResultCode, "ignored", StringComparison.Ordinal))
        {
            return "No newer stable release was found.";
        }

        if (status.UpdateAvailable && IsStableVersion(status.LatestVerifiedVersion))
        {
            return Available(status.LatestVerifiedVersion!, status.CurrentVersion);
        }

        return status.LastSuccessfulCheckAt is not null
            ? UpToDate(status.CurrentVersion)
            : "Updates have not been checked yet.";
    }

    private static string ForCode(string code, UpdateStatusSnapshotV1? status)
    {
        if (string.Equals(code, "release_mutable", StringComparison.Ordinal))
        {
            return "No verified update is available yet.";
        }

        if (string.Equals(code, "already_running", StringComparison.Ordinal))
        {
            return "An update check is already in progress.";
        }

        if (string.Equals(code, "request_timeout", StringComparison.Ordinal)
            || string.Equals(code, "worker_timeout", StringComparison.Ordinal))
        {
            return "The update check timed out. Try again.";
        }

        if (string.Equals(code, "network_unavailable", StringComparison.Ordinal)
            || string.Equals(code, "response_io", StringComparison.Ordinal))
        {
            return NetworkFailure;
        }

        if (string.Equals(code, "verification_unavailable", StringComparison.Ordinal))
        {
            return "Update verification is not available in this build.";
        }

        if (string.Equals(code, "worker_start_failed", StringComparison.Ordinal)
            || string.Equals(code, "gate_unavailable", StringComparison.Ordinal))
        {
            return "The update check could not be started.";
        }

        if (string.Equals(code, "cancelled", StringComparison.Ordinal))
        {
            return "The update check was cancelled.";
        }

        if (string.Equals(code, "ignored", StringComparison.Ordinal))
        {
            return "No newer stable release was found.";
        }

        if (string.Equals(code, "current", StringComparison.Ordinal)
            || string.Equals(code, "not_modified", StringComparison.Ordinal))
        {
            return UpToDate(status?.CurrentVersion);
        }

        if (string.Equals(code, "update_available", StringComparison.Ordinal)
            && status is { UpdateAvailable: true }
            && IsStableVersion(status.LatestVerifiedVersion))
        {
            return Available(status.LatestVerifiedVersion!, status.CurrentVersion);
        }

        if (IsValidationFailure(code))
        {
            return "A release was found, but it could not be verified and was ignored.";
        }

        return NetworkFailure;
    }

    private static bool IsValidationFailure(string code)
        => code.StartsWith("signature_", StringComparison.Ordinal)
            || code.StartsWith("manifest_", StringComparison.Ordinal)
            || code.StartsWith("repository_", StringComparison.Ordinal)
            || code.StartsWith("trust_", StringComparison.Ordinal)
            || code.StartsWith("release_", StringComparison.Ordinal);

    private static bool IsStableVersion(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string Available(string availableVersion, string currentVersion)
        => $"Update {availableVersion} is available. You currently have {currentVersion}.";

    private static string UpToDate(string? currentVersion)
        => $"You're up to date. Current version: {currentVersion ?? "unknown"}.";
}

internal interface IConfiguratorUpdateBridge
{
    Task<UpdateStatusSnapshotV1> QueryStatusAsync(CancellationToken cancellationToken);

    Task<UpdateActionAcceptance> StartCheckAsync(CancellationToken cancellationToken);

    Task<UpdateActionAcceptance> DismissAsync(CancellationToken cancellationToken);

    Task<UpdateActionAcceptance> ClearDismissalAsync(CancellationToken cancellationToken);
}

internal interface IStandaloneUpdateCheckLauncher
{
    Task<StandaloneUpdateCheckResultV1> RunAsync(CancellationToken cancellationToken);
}

internal sealed class SupervisorBridgeUnavailableException : IOException
{
    public SupervisorBridgeUnavailableException()
        : base("The running Supervisor bridge is unavailable.")
    {
    }
}

internal sealed record ConfiguratorUpdateCheckResult(
    UpdateActionAcceptance Acceptance,
    UpdateStatusSnapshotV1? TerminalStatus,
    string Summary);

internal sealed class ConfiguratorUpdateCheckRunner
{
    private readonly IConfiguratorUpdateBridge _bridge;
    private readonly IStandaloneUpdateCheckLauncher? _standaloneLauncher;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _active;

    public ConfiguratorUpdateCheckRunner(
        IConfiguratorUpdateBridge bridge,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _bridge = bridge;
        _standaloneLauncher = null;
        _delay = delay ?? Task.Delay;
    }

    public ConfiguratorUpdateCheckRunner(
        IConfiguratorUpdateBridge bridge,
        IStandaloneUpdateCheckLauncher standaloneLauncher,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _bridge = bridge;
        _standaloneLauncher = standaloneLauncher;
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
            UpdateActionAcceptance acceptance;
            try
            {
                acceptance = await _bridge.StartCheckAsync(cancellationToken);
            }
            catch (SupervisorBridgeUnavailableException) when (_standaloneLauncher is not null)
            {
                var standalone = await _standaloneLauncher.RunAsync(cancellationToken);
                if (standalone.Status is not null)
                {
                    statusObserved?.Invoke(standalone.Status);
                }

                var standaloneAcceptance = new UpdateActionAcceptance(
                    Accepted: standalone.Success,
                    AlreadyInProgress: string.Equals(standalone.ResultCode, "already_running", StringComparison.Ordinal),
                    OperationId: null,
                    Message: standalone.Summary,
                    ResultCode: standalone.ResultCode);
                return new ConfiguratorUpdateCheckResult(
                    standaloneAcceptance,
                    standalone.Status,
                    standalone.Summary);
            }

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
        string? resultCode = null;
        var alreadyInProgress = false;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            operationId = ReadBoundedString(data, "operationId");
            resultCode = ReadBoundedString(data, "resultCode");
            alreadyInProgress = data.TryGetProperty("alreadyInProgress", out var duplicate) && duplicate.ValueKind == JsonValueKind.True;
        }

        return new UpdateActionAcceptance(accepted, alreadyInProgress, operationId, message, resultCode);
    }

    private static async Task<JsonDocument> SendAsync(string command, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync("127.0.0.1", Port, timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException exception) when (exception.SocketErrorCode is
                                                SocketError.ConnectionRefused
                                                or SocketError.HostUnreachable
                                                or SocketError.NetworkUnreachable
                                                or SocketError.AddressNotAvailable)
        {
            throw new SupervisorBridgeUnavailableException();
        }
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
