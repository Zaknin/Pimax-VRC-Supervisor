using Xunit;

[Collection(ControlledNativeValidationCollection.Name)]
public sealed class ControlledNativeValidationRecoveryTests
{
    [Fact]
    public void ReadOnlyCensusValidatesEveryCurrentSourceResidualWithoutMutation()
    {
        SkipUnlessWindows();

        var report = ControlledNativeValidationRecovery.Census();

        Assert.Equal("ReadOnlyCensus", report.Mode);
        Assert.Null(report.RootEvidence);
        Assert.Equal(report.SourceDirectChildCount, report.SourceDirectChildCountAfter);
        Assert.Equal(report.SourceRootIdentityBefore, report.SourceRootIdentityAfter);
        Assert.Equal(report.SourceRootAclBefore, report.SourceRootAclAfter);
        Assert.All(report.Candidates, candidate =>
        {
            Assert.True(candidate.Category is "B" or "C");
            Assert.True(candidate.Outcome is "ValidatedReadOnly" or "ReadOnlyValidationFailed");
            Assert.Null(candidate.QuarantinePath);
        });
        Assert.True(File.Exists(report.ReportPath));
    }

    [Fact]
    public void RecoveryOnlyQuarantinesIdentityProvenCanonicalGuidChildrenAndEmitsPublicReport()
    {
        SkipUnlessWindows();

        var report = ControlledNativeValidationRecovery.Execute();

        Assert.Equal("Recovery", report.Mode);
        Assert.Null(report.RootEvidence);
        Assert.True(report.SourceDirectChildCount >= 0);
        Assert.Equal(0, report.SourceDirectChildCountAfter);
        Assert.Equal(report.SourceRootIdentityBefore, report.SourceRootIdentityAfter);
        Assert.Equal(report.SourceRootAclBefore, report.SourceRootAclAfter);
        Assert.True(File.Exists(report.ReportPath));
        Assert.All(report.Candidates, candidate =>
        {
            Assert.True(candidate.Category is "B" or "C");
            Assert.True(candidate.Outcome is "QuarantinedIntact" or "QuarantinedAmbiguousIntact" or "ExistingQuarantineVerified");
            if (candidate.Category == "C")
            {
                Assert.Equal("QuarantinedAmbiguousIntact", candidate.Outcome);
                Assert.Contains("nested objects were not traversed", candidate.Evidence, StringComparison.Ordinal);
            }
            Assert.NotNull(candidate.Identity);
            Assert.NotNull(candidate.SourcePath);
            Assert.NotNull(candidate.QuarantinePath);
            Assert.True(Directory.Exists(candidate.QuarantinePath!));
            Assert.False(Directory.Exists(candidate.SourcePath!));
        });
        Assert.DoesNotContain(report.Candidates, candidate => candidate.Category == "C" && candidate.Outcome == "MovedButPostRenameVerificationFailed");
    }

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Controlled native validation recovery requires Windows.");
        }
    }
}
