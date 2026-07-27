using Xunit;

[Collection(ControlledNativeValidationCollection.Name)]
public sealed class ControlledNativeValidationRunTests
{
    [Fact]
    public void CreatesGuidChildWithSentinelOnExactFixedNtfsRootAndCleansInventory()
    {
        SkipUnlessWindows();

        string runDirectory;
        using (var run = ControlledNativeValidationRun.Create())
        {
            runDirectory = run.RunDirectory;

            Assert.Equal(ControlledNativeValidationRun.AuthorizedRoot, Path.GetDirectoryName(runDirectory), ignoreCase: true);
            Assert.True(Guid.TryParse(Path.GetFileName(runDirectory), out _));
            Assert.True(File.Exists(run.SentinelPath));
            Assert.NotEqual(default, run.ParentIdentity);
            Assert.NotEqual(default, run.RunIdentity);
            Assert.NotEqual(default, run.SentinelIdentity);
            var sentinel = File.ReadAllText(run.SentinelPath);
            Assert.Contains("parentPath=" + ControlledNativeValidationRun.AuthorizedRoot, sentinel, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("parentIdentity=" + run.ParentIdentity, sentinel, StringComparison.Ordinal);
            Assert.Contains("runPath=" + run.RunDirectory, sentinel, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("runIdentity=" + run.RunIdentity, sentinel, StringComparison.Ordinal);
            Assert.Equal("payload", File.ReadAllText(run.WriteFile("payload.txt", "payload")));
        }

        Assert.False(Directory.Exists(runDirectory));
        Assert.True(Directory.Exists(ControlledNativeValidationRun.AuthorizedRoot));
    }

    [Fact]
    public void ExactCleanupFailsClosedWhenAnUninventoriedDirectChildExists()
    {
        SkipUnlessWindows();

        var run = ControlledNativeValidationRun.Create();
        var runDirectory = run.RunDirectory;
        var untracked = Path.Combine(runDirectory, "untracked.txt");
        try
        {
            File.WriteAllText(untracked, "outside typed inventory");

            Assert.Throws<NativeValidationException>(() => run.Dispose());
            Assert.True(File.Exists(untracked));
            Assert.True(Directory.Exists(runDirectory));
        }
        finally
        {
            if (File.Exists(untracked))
            {
                File.Delete(untracked);
            }

            // The failed preflight leaves the pinned handles open, allowing a retry
            // after the untracked direct child has been removed without path-based
            // directory cleanup.
            run.Dispose();
        }

        Assert.False(Directory.Exists(runDirectory));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("child/file.txt")]
    [InlineData(@"child\file.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData(".")]
    public void RejectsAnyTargetThatIsNotOneDirectRunChild(string unsafeName)
    {
        SkipUnlessWindows();

        using var run = ControlledNativeValidationRun.Create();

        Assert.Throws<ArgumentException>(() => run.WriteFile(unsafeName, "must not be written"));
    }

    [Fact]
    public void RealInternalSymbolicLinkIsParsedTrackedAndDeletedByItsOwnHandle()
    {
        SkipUnlessWindows();

        string runDirectory;
        using (var run = ControlledNativeValidationRun.Create())
        {
            runDirectory = run.RunDirectory;
            var target = run.WriteFile("target.txt", "target");
            var targetIdentity = run.InventoryIdentityForTests("target.txt");
            var link = Path.Combine(runDirectory, "target-link");
            var reparsePoint = run.CreateInternalRelativeSymbolicLinkForTests("target-link", "target.txt");

            Assert.Equal(NativeValidationReparsePointKind.SymbolicLink, reparsePoint.Kind);
            Assert.True(reparsePoint.IsRelative);
            Assert.Equal("target.txt", reparsePoint.SubstituteName);
            Assert.Equal("target.txt", reparsePoint.PrintName);
            Assert.True(File.Exists(link));
            Assert.Equal(targetIdentity, run.InventoryIdentityForTests("target.txt"));
            Assert.Equal("target", File.ReadAllText(target));
        }

        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public void RelativeSymbolicLinkBufferRoundTripsExactTagFlagsAndNames()
    {
        var buffer = NativeValidationFileSystem.BuildRelativeSymbolicLinkReparseBufferForTests("target.txt");

        var parsed = NativeValidationFileSystem.ParseReparsePointBufferForTests(
            buffer,
            buffer.Length,
            NativeValidationFileSystem.SymbolicLinkTagForTests);

        Assert.Equal(NativeValidationFileSystem.SymbolicLinkTagForTests, BitConverter.ToUInt32(buffer, 0));
        Assert.Equal(buffer.Length - 8, BitConverter.ToUInt16(buffer, 4));
        Assert.Equal(1u, BitConverter.ToUInt32(buffer, 16));
        Assert.Equal(NativeValidationReparsePointKind.SymbolicLink, parsed.Kind);
        Assert.True(parsed.IsRelative);
        Assert.Equal("target.txt", parsed.SubstituteName);
        Assert.Equal("target.txt", parsed.PrintName);
    }

    [Fact]
    public void RelativeSymbolicLinkParserRejectsMalformedTruncatedAndInvalidNameLayouts()
    {
        var valid = NativeValidationFileSystem.BuildRelativeSymbolicLinkReparseBufferForTests("target.txt");

        Assert.Throws<InvalidOperationException>(() => NativeValidationFileSystem.ParseReparsePointBufferForTests(
            valid,
            valid.Length - 1,
            NativeValidationFileSystem.SymbolicLinkTagForTests));

        var truncated = valid[..19];
        BitConverter.TryWriteBytes(truncated.AsSpan(4, sizeof(ushort)), checked((ushort)(truncated.Length - 8)));
        Assert.Throws<InvalidOperationException>(() => NativeValidationFileSystem.ParseReparsePointBufferForTests(
            truncated,
            truncated.Length,
            NativeValidationFileSystem.SymbolicLinkTagForTests));

        var invalidOffset = (byte[])valid.Clone();
        BitConverter.TryWriteBytes(invalidOffset.AsSpan(8, sizeof(ushort)), (ushort)2);
        Assert.Throws<InvalidOperationException>(() => NativeValidationFileSystem.ParseReparsePointBufferForTests(
            invalidOffset,
            invalidOffset.Length,
            NativeValidationFileSystem.SymbolicLinkTagForTests));

        var invalidLength = (byte[])valid.Clone();
        BitConverter.TryWriteBytes(invalidLength.AsSpan(10, sizeof(ushort)), checked((ushort)(BitConverter.ToUInt16(valid, 10) + 1)));
        Assert.Throws<InvalidOperationException>(() => NativeValidationFileSystem.ParseReparsePointBufferForTests(
            invalidLength,
            invalidLength.Length,
            NativeValidationFileSystem.SymbolicLinkTagForTests));

        var absoluteFlags = (byte[])valid.Clone();
        BitConverter.TryWriteBytes(absoluteFlags.AsSpan(16, sizeof(uint)), 0u);
        Assert.Throws<InvalidOperationException>(() => NativeValidationFileSystem.ParseReparsePointBufferForTests(
            absoluteFlags,
            absoluteFlags.Length,
            NativeValidationFileSystem.SymbolicLinkTagForTests));
    }

    [Theory]
    [InlineData(@"C:\outside.txt")]
    [InlineData("../outside.txt")]
    [InlineData(@"..\outside.txt")]
    [InlineData("child/target.txt")]
    public void RelativeSymbolicLinkRejectsAbsoluteOrEscapingTargetsBeforeMutation(string unsafeTarget)
    {
        SkipUnlessWindows();

        using var run = ControlledNativeValidationRun.Create();
        _ = run.WriteFile("target.txt", "target");

        Assert.Throws<ArgumentException>(() => run.CreateInternalRelativeSymbolicLinkForTests("target-link", unsafeTarget));
        Assert.DoesNotContain("target-link", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("target-link", run.EnumeratedRunChildNamesForTests(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelativeSymbolicLinkRejectsSubstitutedTargetIdentityBeforeMutation()
    {
        SkipUnlessWindows();

        using var run = ControlledNativeValidationRun.Create();
        _ = run.WriteFile("target.txt", "target");
        _ = run.WriteFile("substitute.txt", "substitute");

        Assert.Throws<InvalidOperationException>(() => run.CreateInternalRelativeSymbolicLinkForTests(
            "target-link",
            "target.txt",
            targetHandleNameForTests: "substitute.txt"));
        Assert.DoesNotContain("target-link", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("target-link", run.EnumeratedRunChildNamesForTests(), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FailedRelativeSymbolicLinkAssignmentLeavesNoOwnerlessObject(bool failBeforeAssignment, bool failAfterAssignment)
    {
        SkipUnlessWindows();

        using var run = ControlledNativeValidationRun.Create();
        _ = run.WriteFile("target.txt", "target");

        Assert.Throws<InvalidOperationException>(() => run.CreateInternalRelativeSymbolicLinkForTests(
            "target-link",
            "target.txt",
            failBeforeAssignment,
            failAfterAssignment));
        Assert.DoesNotContain("target-link", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("target-link", run.EnumeratedRunChildNamesForTests(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SymbolicLinkEvidenceIsNonOwningAndRunDisposalRemovesOnlyTheLinkObject()
    {
        SkipUnlessWindows();

        var authorizedRootBefore = SnapshotAuthorizedRootNames();
        var run = ControlledNativeValidationRun.Create();
        var runDirectory = run.RunDirectory;
        var target = run.WriteFile("target.txt", "target contents");
        var targetIdentity = run.InventoryIdentityForTests("target.txt");

        var evidence = run.CreateInternalRelativeSymbolicLinkForTests("target-link", "target.txt");
        evidence.Dispose();
        DisposeEvidenceAtMethodScope(evidence);
        Assert.Contains("target-link", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(targetIdentity, run.InventoryIdentityForTests("target.txt"));
        Assert.Equal("target contents", File.ReadAllText(target));

        run.Dispose();

        Assert.False(Directory.Exists(runDirectory));
        Assert.Equal(authorizedRootBefore, SnapshotAuthorizedRootNames());
    }

    [Fact]
    public void RealInternalJunctionIsParsedTrackedAndDeletedByItsOwnHandle()
    {
        SkipUnlessWindows();

        string runDirectory;
        using (var run = ControlledNativeValidationRun.Create())
        {
            runDirectory = run.RunDirectory;
            var target = run.CreateDirectoryForTests("junction-target");
            var junction = Path.Combine(runDirectory, "target-junction");
            var reparsePoint = run.CreateInternalMountPointForTests("target-junction", target);
            Assert.Contains("target-junction", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(NativeValidationReparsePointKind.MountPoint, reparsePoint.Kind);
            Assert.False(reparsePoint.IsRelative);
            Assert.Contains("junction-target", reparsePoint.SubstituteName, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(junction));
        }

        Assert.False(Directory.Exists(runDirectory));
    }

    [Fact]
    public void JunctionEvidenceIsNonOwningAndRunDisposePerformsAuthoritativeCleanup()
    {
        SkipUnlessWindows();

        var authorizedRootBefore = SnapshotAuthorizedRootNames();
        var run = ControlledNativeValidationRun.Create();
        var runDirectory = run.RunDirectory;
        var runId = run.RunIdForTests;
        var runObjectIdentity = run.RunObjectIdentityForTests;
        var target = run.CreateDirectoryForTests("junction-target");
        var targetIdentity = run.InventoryIdentityForTests("junction-target");
        var targetContents = Directory.GetFileSystemEntries(target);

        var evidence = run.CreateInternalMountPointForTests("target-junction", target);
        Assert.Contains("target-junction", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        evidence.Dispose();
        Assert.Contains("target-junction", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        DisposeEvidenceAtMethodScope(evidence);
        Assert.Contains("target-junction", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(targetIdentity, run.InventoryIdentityForTests("junction-target"));
        Assert.Equal(targetContents, Directory.GetFileSystemEntries(target));

        run.Dispose();

        Assert.False(Directory.Exists(runDirectory));
        Assert.Equal(authorizedRootBefore, SnapshotAuthorizedRootNames());
        var mutations = run.InventoryMutationsForTests;
        Assert.Equal(Enumerable.Range(1, mutations.Count).Select(value => (long)value), mutations.Select(mutation => mutation.Sequence));
        Assert.All(mutations, mutation =>
        {
            Assert.Equal(runId, mutation.RunId);
            Assert.Equal(runObjectIdentity, mutation.RunObjectIdentity);
            Assert.True(mutation.ManagedThreadId > 0);
            Assert.False(string.IsNullOrWhiteSpace(mutation.Caller));
        });
        Assert.Contains(mutations, mutation => mutation.Operation == "replace"
            && mutation.ObjectName == "target-junction"
            && mutation.ObjectType == NativeValidationInventoryItemKind.ReparsePoint);
        Assert.Contains(mutations, mutation => mutation.Operation == "remove"
            && mutation.ObjectName == "target-junction"
            && mutation.ObjectType == NativeValidationInventoryItemKind.ReparsePoint
            && mutation.Caller == "DeleteExactInventoryItem");
    }

    [Fact]
    public void SuccessfulNativeDeletionRemovesInventoryOnlyAfterPinnedAbsence()
    {
        SkipUnlessWindows();

        using var run = ControlledNativeValidationRun.Create();
        var path = run.CreateDirectoryForTests("delete-success");

        run.DeleteInventoryItemForTests("delete-success");

        Assert.False(Directory.Exists(path));
        Assert.DoesNotContain("delete-success", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete-success", run.EnumeratedRunChildNamesForTests(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(run.InventoryMutationsForTests, mutation => mutation.Operation == "remove"
            && mutation.ObjectName == "delete-success"
            && mutation.Caller == "DeleteExactInventoryItem");
    }

    [Fact]
    public void FailedNativeDeletionPreservesInventoryAndNativeChild()
    {
        SkipUnlessWindows();

        using var run = ControlledNativeValidationRun.Create();
        var path = run.CreateDirectoryForTests("delete-failure");

        var exception = Assert.Throws<InvalidOperationException>(
            () => run.DeleteInventoryItemForTests("delete-failure", failBeforeNativeDeletion: true));

        Assert.Contains("before native mutation", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(path));
        Assert.Contains("delete-failure", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("delete-failure", run.EnumeratedRunChildNamesForTests(), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(run.InventoryMutationsForTests, mutation => mutation.Operation == "remove"
            && mutation.ObjectName == "delete-failure");
    }

    [Fact]
    public void FailedMountPointAssignmentLeavesOwnedOrdinaryInventoryForRunCleanup()
    {
        SkipUnlessWindows();

        string runDirectory;
        using (var run = ControlledNativeValidationRun.Create())
        {
            runDirectory = run.RunDirectory;
            var target = run.CreateDirectoryForTests("junction-target");

            var exception = Assert.Throws<InvalidOperationException>(() => run.CreateInternalMountPointForTests(
                "target-junction",
                target,
                failBeforeMountPointAssignmentForTests: true));

            Assert.Contains("after inventory registration", exception.Message, StringComparison.Ordinal);
            Assert.Contains("target-junction", run.InventoryNamesForTests, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("target-junction", run.EnumeratedRunChildNamesForTests(), StringComparer.OrdinalIgnoreCase);
            Assert.Contains(run.InventoryMutationsForTests, mutation => mutation.Operation == "add"
                && mutation.ObjectName == "target-junction"
                && mutation.ObjectType == NativeValidationInventoryItemKind.Directory);
            Assert.DoesNotContain(run.InventoryMutationsForTests, mutation => mutation.Operation == "replace"
                && mutation.ObjectName == "target-junction");
        }

        Assert.False(Directory.Exists(runDirectory));
    }

    private static void DisposeEvidenceAtMethodScope(NativeValidationReparsePoint evidence)
    {
        using var scopedEvidence = evidence;
    }

    private static string[] SnapshotAuthorizedRootNames()
        => Directory.GetFileSystemEntries(ControlledNativeValidationRun.AuthorizedRoot)
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;


    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Controlled native validation runs require Windows.");
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ControlledNativeValidationCollection
{
    public const string Name = "ControlledNativeValidation";
}
