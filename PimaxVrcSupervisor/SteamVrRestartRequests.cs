namespace PimaxVrcSupervisor;

internal enum SteamVrRestartRequestState
{
    Rejected,
    Accepted,
    Running,
    Succeeded,
    Warning,
    Failed,
    Cancelled
}

internal enum SteamVrRestartRequestDisposition
{
    Accepted,
    DuplicateActive,
    DuplicateTerminal,
    RejectedBusy,
    RejectedUnavailable,
    RejectedInvalid
}

internal sealed record SteamVrRestartRequestEnvelope(
    string RequestId,
    string SourceClientType,
    string SourceClientInstanceId,
    DateTimeOffset CreatedAt);

internal sealed record SteamVrRestartAcceptanceContext(
    SteamVrRuntimeSnapshot? OldRuntime,
    bool ResumeVrChat,
    SteamVrRestartRequestDisposition RejectionDisposition,
    string? RejectionReason)
{
    public bool CanAccept => OldRuntime is not null && RejectionReason is null;

    public static SteamVrRestartAcceptanceContext Accept(
        SteamVrRuntimeSnapshot oldRuntime,
        bool resumeVrChat)
        => new(
            oldRuntime,
            resumeVrChat,
            SteamVrRestartRequestDisposition.Accepted,
            null);

    public static SteamVrRestartAcceptanceContext Reject(
        SteamVrRestartRequestDisposition disposition,
        string reason)
        => new(null, false, disposition, reason);
}

internal sealed record SteamVrRestartRequestSnapshot(
    string RequestId,
    string OperationId,
    string SourceClientType,
    string SourceClientInstanceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    SteamVrRestartRequestState State,
    SteamVrRuntimeIdentity? OldRuntime,
    bool ResumeVrChat,
    bool ShutdownIssuanceAttempted,
    int ShutdownIssueCount,
    string? Result,
    string? Error)
{
    public bool IsTerminal
        => State is SteamVrRestartRequestState.Rejected
            or SteamVrRestartRequestState.Succeeded
            or SteamVrRestartRequestState.Warning
            or SteamVrRestartRequestState.Failed
            or SteamVrRestartRequestState.Cancelled;
}

internal sealed record SteamVrRestartRequestDecision(
    SteamVrRestartRequestDisposition Disposition,
    SteamVrRestartRequestSnapshot? Request,
    string Message)
{
    public bool Accepted => Disposition == SteamVrRestartRequestDisposition.Accepted;
    public bool IsDuplicate
        => Disposition is SteamVrRestartRequestDisposition.DuplicateActive
            or SteamVrRestartRequestDisposition.DuplicateTerminal;
}

internal sealed record SteamVrShutdownIssuanceDecision(
    bool Allowed,
    SteamVrRestartRequestSnapshot? Request,
    string Reason);

internal sealed class SteamVrRestartRequestRegistry
{
    internal const int DefaultMaximumEntries = 256;
    internal static readonly TimeSpan DefaultTerminalRetention = TimeSpan.FromHours(24);

    private readonly object _sync = new();
    private readonly Dictionary<string, SteamVrRestartRequestSnapshot> _requests =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maximumEntries;
    private readonly TimeSpan _terminalRetention;

    public SteamVrRestartRequestRegistry(
        Func<DateTimeOffset>? clock = null,
        int maximumEntries = DefaultMaximumEntries,
        TimeSpan? terminalRetention = null)
    {
        if (maximumEntries < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maximumEntries = maximumEntries;
        _terminalRetention = terminalRetention ?? DefaultTerminalRetention;
        if (_terminalRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(terminalRetention));
        }
    }

    internal int Count
    {
        get
        {
            lock (_sync)
            {
                return _requests.Count;
            }
        }
    }

    public SteamVrRestartRequestDecision Accept(
        SteamVrRestartRequestEnvelope envelope,
        Func<SteamVrRestartAcceptanceContext> createContext)
    {
        if (!TryNormalize(envelope, out var request, out var validationError))
        {
            return new SteamVrRestartRequestDecision(
                SteamVrRestartRequestDisposition.RejectedInvalid,
                null,
                validationError);
        }

        lock (_sync)
        {
            var now = _clock();
            Prune(now, reserveOneEntry: true);
            if (_requests.TryGetValue(request.RequestId, out var existing))
            {
                return Duplicate(existing);
            }

            var active = _requests.Values.FirstOrDefault(candidate => !candidate.IsTerminal);
            var context = active is null
                ? createContext()
                : SteamVrRestartAcceptanceContext.Reject(
                    SteamVrRestartRequestDisposition.RejectedBusy,
                    "A SteamVR restart operation is already active.");
            if (!context.CanAccept)
            {
                var rejected = new SteamVrRestartRequestSnapshot(
                    request.RequestId,
                    OperationId(request.RequestId),
                    request.SourceClientType,
                    request.SourceClientInstanceId,
                    request.CreatedAt,
                    now,
                    now,
                    SteamVrRestartRequestState.Rejected,
                    null,
                    false,
                    ShutdownIssuanceAttempted: false,
                    ShutdownIssueCount: 0,
                    context.RejectionReason,
                    context.RejectionReason);
                _requests.Add(request.RequestId, rejected);
                return new SteamVrRestartRequestDecision(
                    context.RejectionDisposition,
                    rejected,
                    context.RejectionReason ?? "Restart request rejected.");
            }

            var accepted = new SteamVrRestartRequestSnapshot(
                request.RequestId,
                OperationId(request.RequestId),
                request.SourceClientType,
                request.SourceClientInstanceId,
                request.CreatedAt,
                now,
                null,
                SteamVrRestartRequestState.Accepted,
                context.OldRuntime!.Identity,
                context.ResumeVrChat,
                ShutdownIssuanceAttempted: false,
                ShutdownIssueCount: 0,
                null,
                null);
            _requests.Add(request.RequestId, accepted);
            return new SteamVrRestartRequestDecision(
                SteamVrRestartRequestDisposition.Accepted,
                accepted,
                "Graceful VR session restart accepted.");
        }
    }

    public SteamVrRestartRequestSnapshot? MarkRunning(string operationId)
    {
        lock (_sync)
        {
            var current = FindByOperation(operationId);
            if (current is null || current.IsTerminal)
            {
                return current;
            }

            if (current.State == SteamVrRestartRequestState.Accepted)
            {
                current = current with { State = SteamVrRestartRequestState.Running };
                _requests[current.RequestId] = current;
            }

            return current;
        }
    }

    public SteamVrShutdownIssuanceDecision TryBeginShutdownIssuance(
        string operationId,
        SteamVrRuntimeIdentity ownedRuntime,
        bool ownedRuntimePresent,
        bool supervisorShuttingDown)
    {
        lock (_sync)
        {
            var current = FindByOperation(operationId);
            if (current is null)
            {
                return new SteamVrShutdownIssuanceDecision(false, null, "operation-not-found");
            }

            if (supervisorShuttingDown)
            {
                return new SteamVrShutdownIssuanceDecision(false, current, "supervisor-shutdown");
            }

            if (current.IsTerminal)
            {
                return new SteamVrShutdownIssuanceDecision(false, current, "operation-terminal");
            }

            if (current.State is not (SteamVrRestartRequestState.Accepted or SteamVrRestartRequestState.Running))
            {
                return new SteamVrShutdownIssuanceDecision(false, current, "operation-not-active");
            }

            if (current.OldRuntime != ownedRuntime)
            {
                return new SteamVrShutdownIssuanceDecision(false, current, "runtime-ownership-mismatch");
            }

            if (!ownedRuntimePresent)
            {
                return new SteamVrShutdownIssuanceDecision(false, current, "owned-runtime-not-present");
            }

            if (current.ShutdownIssuanceAttempted)
            {
                return new SteamVrShutdownIssuanceDecision(false, current, "shutdown-already-attempted");
            }

            current = current with { ShutdownIssuanceAttempted = true };
            _requests[current.RequestId] = current;
            return new SteamVrShutdownIssuanceDecision(true, current, "shutdown-issuance-claimed");
        }
    }

    public SteamVrRestartRequestSnapshot? RecordShutdownProcessCreation(
        string operationId,
        bool succeeded,
        string? error)
    {
        lock (_sync)
        {
            var current = FindByOperation(operationId);
            if (current is null)
            {
                return null;
            }

            current = current with
            {
                ShutdownIssueCount = succeeded ? 1 : current.ShutdownIssueCount,
                Error = succeeded ? current.Error : error
            };
            _requests[current.RequestId] = current;
            return current;
        }
    }

    public SteamVrRestartRequestSnapshot? Complete(
        string operationId,
        SteamVrRestartRequestState terminalState,
        string result,
        string? error)
    {
        if (terminalState is not (
            SteamVrRestartRequestState.Succeeded
            or SteamVrRestartRequestState.Warning
            or SteamVrRestartRequestState.Failed
            or SteamVrRestartRequestState.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalState));
        }

        lock (_sync)
        {
            var current = FindByOperation(operationId);
            if (current is null || current.IsTerminal)
            {
                return current;
            }

            current = current with
            {
                State = terminalState,
                CompletedAt = _clock(),
                Result = result,
                Error = error
            };
            _requests[current.RequestId] = current;
            Prune(_clock(), reserveOneEntry: false);
            return current;
        }
    }

    internal SteamVrRestartRequestSnapshot? Get(string requestId)
    {
        if (!Guid.TryParse(requestId, out var parsed))
        {
            return null;
        }

        lock (_sync)
        {
            _requests.TryGetValue(parsed.ToString("N"), out var request);
            return request;
        }
    }

    private static bool TryNormalize(
        SteamVrRestartRequestEnvelope envelope,
        out SteamVrRestartRequestEnvelope request,
        out string error)
    {
        request = envelope;
        if (!Guid.TryParse(envelope.RequestId, out var requestId))
        {
            error = "Restart request requires a valid GUID requestId created by explicit confirmation.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(envelope.SourceClientType)
            || string.IsNullOrWhiteSpace(envelope.SourceClientInstanceId))
        {
            error = "Restart request requires sourceClientType and sourceClientInstanceId.";
            return false;
        }

        if (envelope.SourceClientType.Length > 64 || envelope.SourceClientInstanceId.Length > 128)
        {
            error = "Restart request source metadata is too long.";
            return false;
        }

        request = envelope with
        {
            RequestId = requestId.ToString("N"),
            SourceClientType = envelope.SourceClientType.Trim(),
            SourceClientInstanceId = envelope.SourceClientInstanceId.Trim()
        };
        error = "";
        return true;
    }

    private static SteamVrRestartRequestDecision Duplicate(SteamVrRestartRequestSnapshot existing)
    {
        var disposition = existing.IsTerminal
            ? SteamVrRestartRequestDisposition.DuplicateTerminal
            : SteamVrRestartRequestDisposition.DuplicateActive;
        var message = existing.IsTerminal
            ? existing.Result ?? existing.Error ?? "Restart request already completed."
            : "Restart request is already active.";
        return new SteamVrRestartRequestDecision(disposition, existing, message);
    }

    private SteamVrRestartRequestSnapshot? FindByOperation(string operationId)
        => _requests.Values.FirstOrDefault(
            request => string.Equals(request.OperationId, operationId, StringComparison.Ordinal));

    private void Prune(DateTimeOffset now, bool reserveOneEntry)
    {
        var expired = _requests.Values
            .Where(request => request.IsTerminal
                && request.CompletedAt is { } completedAt
                && now - completedAt > _terminalRetention)
            .OrderBy(request => request.CompletedAt)
            .Select(request => request.RequestId)
            .ToArray();
        foreach (var requestId in expired)
        {
            _requests.Remove(requestId);
        }

        var targetCount = reserveOneEntry ? _maximumEntries - 1 : _maximumEntries;
        foreach (var request in _requests.Values
                     .Where(request => request.IsTerminal)
                     .OrderBy(request => request.CompletedAt ?? request.ReceivedAt)
                     .ToArray())
        {
            if (_requests.Count <= targetCount)
            {
                break;
            }

            _requests.Remove(request.RequestId);
        }
    }

    private static string OperationId(string requestId)
        => "vrrestart-" + requestId;
}
