using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class UpdateStatusUserMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("release_mutable", "No verified update is available yet.")]
    [InlineData("already_running", "An update check is already in progress.")]
    [InlineData("network_unavailable", "Couldn't check for updates. Check your connection and try again.")]
    [InlineData("request_timeout", "The update check timed out. Try again.")]
    [InlineData("signature_invalid", "A release was found, but it could not be verified and was ignored.")]
    [InlineData("verification_unavailable", "Update verification is not available in this build.")]
    [InlineData("worker_start_failed", "The update check could not be started.")]
    [InlineData("cancelled", "The update check was cancelled.")]
    public void FailedResultCodesUseHumanReadablePrimaryMessages(string code, string expected)
    {
        Assert.Equal(expected, UpdateStatusUserMessage.ForResult(code, FailedStatus(code)));
    }

    [Fact]
    public void VerifiedCurrentStatusUsesUpToDateMessage()
    {
        var status = Status(
            latest: "1.3.1",
            updateAvailable: false,
            lastSuccessfulCheckAt: Now,
            lastErrorCode: null,
            verificationConfigured: true);

        Assert.Equal("You're up to date. Current version: 1.3.1.", UpdateStatusUserMessage.ForResult("current", status));
    }

    [Fact]
    public void VerifiedAvailableStatusIncludesAvailableAndCurrentVersions()
    {
        var status = Status(
            latest: "1.4.0",
            updateAvailable: true,
            lastSuccessfulCheckAt: Now,
            lastErrorCode: null,
            verificationConfigured: true);

        Assert.Equal("Update 1.4.0 is available. You currently have 1.3.1.", UpdateStatusUserMessage.ForResult("update_available", status));
    }

    [Fact]
    public void FailedStatusNeverUsesUpToDateMessage()
    {
        var status = FailedStatus("network_unavailable") with { LastSuccessfulCheckAt = Now };

        var message = UpdateStatusUserMessage.ForStatus(status);

        Assert.DoesNotContain("up to date", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Couldn't check for updates. Check your connection and try again.", message);
    }

    [Fact]
    public void InternalResultCodeRemainsInStatusButIsNotInPrimaryMessage()
    {
        var status = FailedStatus("release_mutable");

        Assert.Equal("release_mutable", status.LastErrorCode);
        Assert.DoesNotContain("release_mutable", UpdateStatusUserMessage.ForStatus(status), StringComparison.Ordinal);
    }

    [Fact]
    public void NeverCheckedStatusUsesClearMessage()
    {
        var status = Status(
            latest: null,
            updateAvailable: false,
            lastSuccessfulCheckAt: null,
            lastErrorCode: null,
            verificationConfigured: true)
        with
        {
            LastAttemptAt = null
        };

        Assert.Equal("Updates have not been checked yet.", UpdateStatusUserMessage.ForStatus(status));
    }

    [Fact]
    public void NoEligibleStableReleaseUsesClearMessage()
    {
        Assert.Equal("No newer stable release was found.", UpdateStatusUserMessage.ForResult("ignored", FailedStatus("ignored")));
    }

    private static UpdateStatusSnapshotV1 FailedStatus(string code)
        => Status(
            latest: null,
            updateAvailable: false,
            lastSuccessfulCheckAt: null,
            lastErrorCode: code,
            verificationConfigured: code != "verification_unavailable");

    private static UpdateStatusSnapshotV1 Status(
        string? latest,
        bool updateAvailable,
        DateTimeOffset? lastSuccessfulCheckAt,
        string? lastErrorCode,
        bool verificationConfigured)
        => new(
            SchemaVersion: 1,
            Policy: "Disabled",
            Channel: "Stable",
            CurrentVersion: "1.3.1",
            LatestVerifiedVersion: latest,
            UpdateAvailable: updateAvailable,
            Dismissed: false,
            DismissedVersion: null,
            LastAttemptAt: Now,
            LastSuccessfulCheckAt: lastSuccessfulCheckAt,
            LastErrorCode: lastErrorCode,
            LastErrorSummary: lastErrorCode,
            VerificationConfigured: verificationConfigured,
            AutomaticCheckDue: false,
            CheckInProgress: false,
            Operation: null);
}
