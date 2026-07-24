using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

/// <summary>
/// One-time, test-only recovery of direct GUID validation residuals. The only
/// mutation is a native, handle-relative, no-replace rename from the fixed
/// source root into the fixed quarantine root. It never deletes or traverses a
/// candidate.
/// </summary>
internal static class ControlledNativeValidationRecovery
{
    internal static string SourceRoot => ControlledNativeValidationRun.AuthorizedRoot;
    internal static string QuarantineRoot => Path.Combine(ControlledNativeValidationRun.AuthorizedVolumeRoot, QuarantineRootName);
    private static string SourceRootName => ControlledNativeValidationRun.AuthorizedRootName;
    private const string QuarantineRootName = "PimaxVrcSupervisor-PrivilegedNativeValidation-RecoveryQuarantine";
    private const string ReportPrefix = "PimaxVrcSupervisor-PrivilegedNativeValidation-RecoveryReport-";

    internal static NativeValidationRecoveryReport Census()
        => ExecuteCore(mutate: false);

    internal static NativeValidationRecoveryReport Execute()
        => ExecuteCore(mutate: true);

    private static NativeValidationRecoveryReport ExecuteCore(bool mutate)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Controlled native validation recovery requires Windows.");
        }

        var startedUtc = DateTimeOffset.UtcNow;
        var recoveryId = Guid.NewGuid();
        var candidates = new List<NativeValidationRecoveryCandidate>();
        var sourceDirectChildCount = 0;
        var sourceDirectChildCountAfter = 0;
        var quarantineDirectChildCount = 0;
        NativeValidationRecoveryRootEvidence? rootEvidence = null;
        NativeValidationFileIdentity? sourceRootIdentityBefore = null;
        NativeValidationFileIdentity? sourceRootIdentityAfter = null;
        string? sourceRootAclBefore = null;
        string? sourceRootAclAfter = null;

        try
        {
            using var volume = NativeValidationFileSystem.ValidateDirectoryHandle(
                NativeValidationFileSystem.OpenVolumeAnchor(ControlledNativeValidationRun.AuthorizedVolumeRoot));
            NativeValidationFileSystem.ValidateFixedNtfsVolume(volume.Handle, ControlledNativeValidationRun.AuthorizedVolumeRoot);

            using var source = NativeValidationFileSystem.OpenRelativeDirectory(volume.Handle, SourceRootName, writable: true);
            NativeValidationFileSystem.VerifyDirectoryLocation(source, SourceRoot);
            sourceRootIdentityBefore = NativeValidationFileSystem.ReadIdentity(source.Handle);
            sourceRootAclBefore = ReadSourceRootAclSddl();

            PinnedNativeValidationDirectory? quarantine = null;
            if (mutate)
            {
                quarantine = NativeValidationFileSystem.OpenOrCreateRelativeDirectoryForRecoverySecurity(volume.Handle, QuarantineRootName);
                NativeValidationFileSystem.VerifyDirectoryLocation(quarantine, QuarantineRoot);
                ValidateSameVolume(source, quarantine);
                PrepareAndValidateQuarantineRootAcl(quarantine);
                NativeValidationFileSystem.VerifyPinnedDirectory(quarantine, QuarantineRoot);
            }

            try
            {
                var sourceEntries = NativeValidationFileSystem.EnumerateDirectChildren(source.Handle);
                sourceDirectChildCount = sourceEntries.Count;
                foreach (var entry in sourceEntries)
                {
                    if (!IsCanonicalGuidName(entry.Name))
                    {
                        candidates.Add(new NativeValidationRecoveryCandidate(
                            EntryName: entry.Name,
                            Category: "C",
                            Outcome: "NotMovedUnsafeDirectName",
                            SourcePath: null,
                            QuarantinePath: null,
                            Identity: null,
                            Evidence: $"Direct enumeration returned non-canonical-GUID name with attributes 0x{entry.Attributes:X8}; no open or mutation was attempted."));
                        continue;
                    }

                    candidates.Add(InspectOrRecoverDirectGuidChild(source, quarantine, entry.Name, mutate));
                }

                if (mutate && sourceDirectChildCount == 0)
                {
                    foreach (var entry in NativeValidationFileSystem.EnumerateDirectChildren(quarantine!.Handle))
                    {
                        candidates.Add(VerifyExistingQuarantineChild(source, quarantine, entry.Name));
                    }
                }

                sourceDirectChildCountAfter = NativeValidationFileSystem.EnumerateDirectChildren(source.Handle).Count;
                quarantineDirectChildCount = quarantine is null
                    ? 0
                    : NativeValidationFileSystem.EnumerateDirectChildren(quarantine.Handle).Count;
                sourceRootIdentityAfter = NativeValidationFileSystem.ReadIdentity(source.Handle);
                sourceRootAclAfter = ReadSourceRootAclSddl();
                if (sourceRootIdentityBefore != sourceRootIdentityAfter
                    || !string.Equals(sourceRootAclBefore, sourceRootAclAfter, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The authorized controlled root identity or ACL changed during recovery processing.");
                }
            }
            finally
            {
                quarantine?.Dispose();
            }
        }
        catch (Exception exception)
        {
            rootEvidence = new NativeValidationRecoveryRootEvidence(
                Outcome: "BlockedCategoryC",
                Evidence: DescribeException(exception));
        }

        var report = new NativeValidationRecoveryReport(
            RecoveryId: recoveryId,
            Mode: mutate ? "Recovery" : "ReadOnlyCensus",
            StartedUtc: startedUtc,
            CompletedUtc: DateTimeOffset.UtcNow,
            SourceRoot: SourceRoot,
            QuarantineRoot: QuarantineRoot,
            SourceDirectChildCount: sourceDirectChildCount,
            SourceDirectChildCountAfter: sourceDirectChildCountAfter,
            QuarantineDirectChildCount: quarantineDirectChildCount,
            SourceRootIdentityBefore: sourceRootIdentityBefore,
            SourceRootIdentityAfter: sourceRootIdentityAfter,
            SourceRootAclBefore: sourceRootAclBefore,
            SourceRootAclAfter: sourceRootAclAfter,
            RootEvidence: rootEvidence,
            Candidates: candidates);
        var reportPath = WritePublicReport(report);
        return report with { ReportPath = reportPath };
    }

    private static NativeValidationRecoveryCandidate InspectOrRecoverDirectGuidChild(
        PinnedNativeValidationDirectory source,
        PinnedNativeValidationDirectory? quarantine,
        string childName,
        bool mutate)
    {
        string? sourcePath = null;
        string? quarantinePath = null;
        NativeValidationFileIdentity? identity = null;
        var renamed = false;

        try
        {
            // FILE_OPEN_REPARSE_POINT is used by this helper. The handle is rejected
            // unless it is an ordinary directory, so no candidate target is followed.
            using var child = NativeValidationFileSystem.OpenRelativeDirectoryForRecoveryRename(source.Handle, childName);
            identity = NativeValidationFileSystem.ReadIdentity(child);
            NativeValidationFileSystem.VerifyOrdinaryDirectory(child, identity.Value);
            if (identity.Value.FileIndex == 0 || identity.Value.VolumeSerialNumber != source.Identity.VolumeSerialNumber)
            {
                throw new InvalidOperationException("The direct GUID child had an invalid or cross-volume native identity.");
            }

            sourcePath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(child));
            var expectedSourcePath = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(source.FinalPath), childName);
            if (!string.Equals(sourcePath, expectedSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The opened child final path did not prove direct source containment. Expected='{expectedSourcePath}' Actual='{sourcePath}'.");
            }

            var topologyEvidence = ValidateResidualTopology(source, child, childName, sourcePath, identity.Value);
            if (!mutate)
            {
                return new NativeValidationRecoveryCandidate(
                    EntryName: childName,
                    Category: "B",
                    Outcome: "ValidatedReadOnly",
                    SourcePath: sourcePath,
                    QuarantinePath: null,
                    Identity: identity,
                    Evidence: topologyEvidence + " No mutation was attempted.");
            }

            if (quarantine is null)
            {
                throw new InvalidOperationException("A validated recovery mutation did not have a pinned quarantine root.");
            }

            var destinationName = childName + "-recovered-" + Guid.NewGuid().ToString("N");
            NativeValidationFileSystem.RenameRelativeNoReplace(child, quarantine.Handle, destinationName);
            renamed = true;

            var afterRenameIdentity = NativeValidationFileSystem.ReadIdentity(child);
            NativeValidationFileSystem.VerifyOrdinaryDirectory(child, afterRenameIdentity);
            if (!NativeValidationFileSystem.SameObjectIdentity(identity.Value, afterRenameIdentity))
            {
                throw new InvalidOperationException("The native identity changed during recovery rename.");
            }

            quarantinePath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(child));
            var expectedQuarantinePath = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(quarantine.FinalPath), destinationName);
            if (!string.Equals(quarantinePath, expectedQuarantinePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The renamed child final path did not prove quarantine containment. Expected='{expectedQuarantinePath}' Actual='{quarantinePath}'.");
            }

            using (var reopened = NativeValidationFileSystem.OpenRelativeDirectoryForRecoveryRename(quarantine.Handle, destinationName))
            {
                var reopenedIdentity = NativeValidationFileSystem.ReadIdentity(reopened);
                NativeValidationFileSystem.VerifyOrdinaryDirectory(reopened, reopenedIdentity);
                var reopenedPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(reopened));
                if (!NativeValidationFileSystem.SameObjectIdentity(identity.Value, reopenedIdentity)
                    || !string.Equals(reopenedPath, expectedQuarantinePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The relocated child could not be re-opened with its original identity at its exact quarantine location.");
                }
            }

            NativeValidationFileSystem.VerifyDirectDirectoryChildIsAbsent(source.Handle, childName);
            NativeValidationFileSystem.FlushDirectory(source.Handle);
            NativeValidationFileSystem.FlushDirectory(quarantine.Handle);

            return new NativeValidationRecoveryCandidate(
                EntryName: childName,
                Category: "B",
                Outcome: "QuarantinedIntact",
                SourcePath: sourcePath,
                QuarantinePath: quarantinePath,
                Identity: identity,
                Evidence: topologyEvidence + " Direct source containment, same native identity, exact quarantine location, source absence, and both parent flushes were verified.");
        }
        catch (Exception exception)
        {
            if (mutate
                && !renamed
                && quarantine is not null
                && identity is { } provenIdentity
                && sourcePath is not null)
            {
                try
                {
                    return QuarantineAmbiguousDirectGuidChild(
                        source,
                        quarantine,
                        childName,
                        sourcePath,
                        provenIdentity,
                        DescribeException(exception));
                }
                catch (Exception quarantineException)
                {
                    return new NativeValidationRecoveryCandidate(
                        EntryName: childName,
                        Category: "C",
                        Outcome: "NotMovedValidationAndAmbiguousQuarantineFailed",
                        SourcePath: sourcePath,
                        QuarantinePath: null,
                        Identity: identity,
                        Evidence: DescribeException(exception) + " Ambiguous intact quarantine also failed: " + DescribeException(quarantineException));
                }
            }

            return new NativeValidationRecoveryCandidate(
                EntryName: childName,
                Category: "C",
                Outcome: renamed
                    ? "MovedButPostRenameVerificationFailed"
                    : mutate ? "NotMovedValidationFailed" : "ReadOnlyValidationFailed",
                SourcePath: sourcePath,
                QuarantinePath: quarantinePath,
                Identity: identity,
                Evidence: DescribeException(exception));
        }
    }

    private static NativeValidationRecoveryCandidate QuarantineAmbiguousDirectGuidChild(
        PinnedNativeValidationDirectory source,
        PinnedNativeValidationDirectory quarantine,
        string childName,
        string sourcePath,
        NativeValidationFileIdentity expectedIdentity,
        string validationEvidence)
    {
        using var child = NativeValidationFileSystem.OpenRelativeDirectoryForRecoveryRename(source.Handle, childName);
        var currentIdentity = NativeValidationFileSystem.ReadIdentity(child);
        NativeValidationFileSystem.VerifyOrdinaryDirectory(child, currentIdentity);
        var currentPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(child));
        if (!NativeValidationFileSystem.SameObjectIdentity(expectedIdentity, currentIdentity)
            || !string.Equals(currentPath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The ambiguous direct GUID child changed identity or location before intact quarantine.");
        }

        var destinationName = childName + "-ambiguous-" + Guid.NewGuid().ToString("N");
        NativeValidationFileSystem.RenameRelativeNoReplace(child, quarantine.Handle, destinationName);
        var quarantinePath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(child));
        var expectedQuarantinePath = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(quarantine.FinalPath), destinationName);
        var afterIdentity = NativeValidationFileSystem.ReadIdentity(child);
        if (!NativeValidationFileSystem.SameObjectIdentity(expectedIdentity, afterIdentity)
            || !string.Equals(quarantinePath, expectedQuarantinePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The ambiguous direct GUID child did not retain identity at its exact quarantine location.");
        }

        NativeValidationFileSystem.VerifyDirectDirectoryChildIsAbsent(source.Handle, childName);
        NativeValidationFileSystem.FlushDirectory(source.Handle);
        NativeValidationFileSystem.FlushDirectory(quarantine.Handle);
        return new NativeValidationRecoveryCandidate(
            EntryName: childName,
            Category: "C",
            Outcome: "QuarantinedAmbiguousIntact",
            SourcePath: sourcePath,
            QuarantinePath: quarantinePath,
            Identity: expectedIdentity,
            Evidence: validationEvidence + " Direct canonical GUID containment and stable native identity were sufficient only for non-destructive intact quarantine; nested objects were not traversed.");
    }

    private static string ValidateResidualTopology(
        PinnedNativeValidationDirectory source,
        Microsoft.Win32.SafeHandles.SafeFileHandle child,
        string childName,
        string sourcePath,
        NativeValidationFileIdentity childIdentity)
    {
        const string sentinelName = ".pimax-vrc-supervisor-native-validation-sentinel";
        const string targetName = "junction-target";
        const string junctionName = "target-junction";
        var expectedNames = new[] { sentinelName, targetName, junctionName };
        var actualEntries = NativeValidationFileSystem.EnumerateDirectChildren(child);
        var actualNames = actualEntries
            .Select(entry => entry.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!actualNames.Contains(sentinelName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The residual did not retain the required ownership sentinel. Actual=[{string.Join(',', actualNames)}].");
        }

        using var sentinel = NativeValidationFileSystem.OpenRelativeFile(child, sentinelName);
        var sentinelIdentity = NativeValidationFileSystem.ReadIdentity(sentinel);
        NativeValidationFileSystem.VerifyRegularFile(sentinel, sentinelIdentity);
        var sentinelPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(sentinel));
        if (!string.Equals(sentinelPath, Path.Combine(sourcePath, sentinelName), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The residual sentinel did not prove exact direct-run containment.");
        }

        var sentinelContents = NativeValidationFileSystem.ReadBoundedUtf8File(sentinel, maximumBytes: 4096);
        var expectedSentinel = string.Join('\n',
            "PimaxVrcSupervisor controlled native validation run",
            $"run={childName}",
            $"parentPath={NativeValidationFileSystem.NormalizeFinalPath(source.FinalPath)}",
            $"parentIdentity={source.Identity}",
            $"runPath={sourcePath}",
            $"runIdentity={childIdentity}");
        if (!string.Equals(sentinelContents, expectedSentinel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The residual sentinel did not exactly bind the current GUID, source root, run identity, and canonical paths.");
        }

        if (!actualNames.SequenceEqual(expectedNames.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            return $"Read-only census validated canonical GUID naming, exact native root/run/sentinel identities, exact sentinel binding, and direct available inventory=[{string.Join(',', actualNames)}]. Nested objects were not traversed; the owned run is authorized only for intact quarantine.";
        }

        using var target = NativeValidationFileSystem.OpenRelativeDirectory(child, targetName, writable: false);
        NativeValidationFileSystem.VerifyDirectoryLocation(target, Path.Combine(sourcePath, targetName));
        var junctionEntry = actualEntries.Single(entry => string.Equals(entry.Name, junctionName, StringComparison.OrdinalIgnoreCase));
        const uint fileAttributeReparsePoint = 0x00000400;
        if ((junctionEntry.Attributes & fileAttributeReparsePoint) == 0)
        {
            using var incompleteJunction = NativeValidationFileSystem.OpenRelativeDirectory(child, junctionName, writable: false);
            NativeValidationFileSystem.VerifyDirectoryLocation(incompleteJunction, Path.Combine(sourcePath, junctionName));
            if (NativeValidationFileSystem.EnumerateDirectChildren(incompleteJunction.Handle).Count != 0
                || incompleteJunction.Identity.VolumeSerialNumber != childIdentity.VolumeSerialNumber)
            {
                throw new InvalidOperationException("The incomplete residual junction object was not an exact empty same-run ordinary directory.");
            }

            return "Read-only census validated canonical GUID naming, exact native root/run/sentinel/target identities, exact sentinel binding, exact three-item available inventory evidence, same-volume containment, and an empty ordinary target-junction object retained by a pre-assignment failure.";
        }

        using var junction = NativeValidationFileSystem.OpenRelativeReparseObjectForInspection(child, junctionName);
        var junctionIdentity = NativeValidationFileSystem.ReadIdentity(junction);
        var reparsePoint = NativeValidationFileSystem.ReadReparsePoint(junction, junctionIdentity);
        var junctionPath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(junction));
        if (reparsePoint.Kind != NativeValidationReparsePointKind.MountPoint
            || reparsePoint.IsRelative
            || !string.Equals(reparsePoint.SubstituteName, NativeValidationFileSystem.ToMountPointSubstituteName(target.FinalPath), StringComparison.Ordinal)
            || !string.Equals(reparsePoint.PrintName, NativeValidationFileSystem.NormalizeFinalPath(target.FinalPath), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(junctionPath, Path.Combine(sourcePath, junctionName), StringComparison.OrdinalIgnoreCase)
            || target.Identity.VolumeSerialNumber != childIdentity.VolumeSerialNumber
            || junctionIdentity.VolumeSerialNumber != childIdentity.VolumeSerialNumber)
        {
            throw new InvalidOperationException("The residual junction did not prove an exact same-run, same-volume ordinary target without traversal.");
        }

        return "Read-only census validated canonical GUID naming, exact native root/run/sentinel/target/junction identities, exact sentinel binding, exact three-item available inventory evidence, same-volume containment, and a mount-point target confined to junction-target.";
    }

    private static NativeValidationRecoveryCandidate VerifyExistingQuarantineChild(
        PinnedNativeValidationDirectory source,
        PinnedNativeValidationDirectory quarantine,
        string quarantineName)
    {
        string? sourcePath = null;
        string? quarantinePath = null;
        NativeValidationFileIdentity? identity = null;
        try
        {
            if (!TryParseRecoveryDestinationName(quarantineName, out var sourceName))
            {
                throw new InvalidOperationException("The quarantine contained a name that was not a recovery-generated GUID destination.");
            }

            sourcePath = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(source.FinalPath), sourceName);
            using var child = NativeValidationFileSystem.OpenRelativeDirectoryForDeletion(quarantine.Handle, quarantineName);
            identity = NativeValidationFileSystem.ReadIdentity(child);
            NativeValidationFileSystem.VerifyOrdinaryDirectory(child, identity.Value);
            quarantinePath = NativeValidationFileSystem.NormalizeFinalPath(NativeValidationFileSystem.GetFinalPath(child));
            var expectedQuarantinePath = Path.Combine(NativeValidationFileSystem.NormalizeFinalPath(quarantine.FinalPath), quarantineName);
            if (identity.Value.FileIndex == 0
                || identity.Value.VolumeSerialNumber != quarantine.Identity.VolumeSerialNumber
                || !string.Equals(quarantinePath, expectedQuarantinePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The existing quarantine child did not prove its exact direct location and native identity.");
            }

            NativeValidationFileSystem.VerifyDirectDirectoryChildIsAbsent(source.Handle, sourceName);
            NativeValidationFileSystem.FlushDirectory(source.Handle);
            NativeValidationFileSystem.FlushDirectory(quarantine.Handle);
            return new NativeValidationRecoveryCandidate(
                EntryName: sourceName,
                Category: "B",
                Outcome: "ExistingQuarantineVerified",
                SourcePath: sourcePath,
                QuarantinePath: quarantinePath,
                Identity: identity,
                Evidence: "The recovery-generated quarantine child was re-opened with FILE_OPEN_REPARSE_POINT, matched its direct pinned destination location and identity, had no same-named source child, and both parents were flushed.");
        }
        catch (Exception exception)
        {
            return new NativeValidationRecoveryCandidate(
                EntryName: quarantineName,
                Category: "C",
                Outcome: "ExistingQuarantineVerificationFailed",
                SourcePath: sourcePath,
                QuarantinePath: quarantinePath,
                Identity: identity,
                Evidence: DescribeException(exception));
        }
    }


    private static string ReadSourceRootAclSddl()
    {
        const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;
        var security = new DirectoryInfo(SourceRoot).GetAccessControl(sections);
        return security.GetSecurityDescriptorSddlForm(sections);
    }

    private static void PrepareAndValidateQuarantineRootAcl(PinnedNativeValidationDirectory quarantine)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The recovery process did not have a Windows user SID for quarantine ACL ownership.");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var expectedSids = new HashSet<string>(StringComparer.Ordinal)
        {
            currentUser.Value,
            administrators.Value,
            localSystem.Value,
        };

        // Only the explicitly authorized quarantine root is hardened. Do not set
        // Owner: an owner write requires a privilege that a standard owner need not
        // hold, whereas that owner has the required right to set the root DACL.
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { currentUser, administrators, localSystem })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        NativeValidationFileSystem.SetProtectedDacl(quarantine.Handle, security);
        var actualSecurity = NativeValidationFileSystem.ReadDirectorySecurity(quarantine.Handle);
        if (!actualSecurity.AreAccessRulesProtected
            || actualSecurity.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || owner != currentUser)
        {
            throw new InvalidOperationException("The quarantine root ACL was not protected and owned by the current recovery user.");
        }

        var rules = actualSecurity.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (rules.Length != expectedSids.Count
            || rules.Any(rule => rule.AccessControlType != AccessControlType.Allow
                || rule.IdentityReference is not SecurityIdentifier sid
                || !expectedSids.Contains(sid.Value)
                || (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl
                || rule.InheritanceFlags != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
                || rule.PropagationFlags != PropagationFlags.None))
        {
            throw new InvalidOperationException("The quarantine root ACL did not exactly grant inheritable full control only to the recovery user, Administrators, and LocalSystem.");
        }
    }

    private static void ValidateSameVolume(PinnedNativeValidationDirectory source, PinnedNativeValidationDirectory quarantine)
    {
        if (source.Identity.VolumeSerialNumber == 0
            || source.Identity.VolumeSerialNumber != quarantine.Identity.VolumeSerialNumber)
        {
            throw new InvalidOperationException("Source and quarantine roots are not pinned to the same valid NTFS volume.");
        }
    }

    private static bool TryParseRecoveryDestinationName(string name, out string sourceName)
    {
        const string recoveredMarker = "-recovered-";
        const string ambiguousMarker = "-ambiguous-";
        var marker = name.Length > 36 && name.AsSpan(36).StartsWith(recoveredMarker, StringComparison.Ordinal)
            ? recoveredMarker
            : name.Length > 36 && name.AsSpan(36).StartsWith(ambiguousMarker, StringComparison.Ordinal)
                ? ambiguousMarker
                : null;
        if (marker is null
            || name.Length != 36 + marker.Length + 32
            || !IsCanonicalGuidName(name[..36])
            || !Guid.TryParseExact(name[(36 + marker.Length)..], "N", out _))
        {
            sourceName = string.Empty;
            return false;
        }

        sourceName = name[..36];
        return true;
    }

    private static bool IsCanonicalGuidName(string name)
        => Guid.TryParseExact(name, "D", out var guid)
            && string.Equals(guid.ToString("D"), name, StringComparison.OrdinalIgnoreCase);

    private static string DescribeException(Exception exception)
        => exception is NativeValidationException native
            ? $"{native.GetType().Name}; NTSTATUS=0x{native.Status:X8}; {native.Message}"
            : $"{exception.GetType().Name}; {exception.Message}";

    private static string WritePublicReport(NativeValidationRecoveryReport report)
    {
        var publicReportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(publicReportDirectory))
        {
            throw new InvalidOperationException("The current recovery user profile directory is unavailable for the bounded recovery report.");
        }

        Directory.CreateDirectory(publicReportDirectory);
        var path = Path.Combine(
            publicReportDirectory,
            ReportPrefix + report.StartedUtc.ToString("yyyyMMddTHHmmssfff'Z'") + "-" + report.RecoveryId.ToString("N") + ".json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}

internal sealed record NativeValidationRecoveryReport(
    Guid RecoveryId,
    string Mode,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string SourceRoot,
    string QuarantineRoot,
    int SourceDirectChildCount,
    int SourceDirectChildCountAfter,
    int QuarantineDirectChildCount,
    NativeValidationFileIdentity? SourceRootIdentityBefore,
    NativeValidationFileIdentity? SourceRootIdentityAfter,
    string? SourceRootAclBefore,
    string? SourceRootAclAfter,
    NativeValidationRecoveryRootEvidence? RootEvidence,
    IReadOnlyList<NativeValidationRecoveryCandidate> Candidates)
{
    public string? ReportPath { get; init; }
}

internal sealed record NativeValidationRecoveryRootEvidence(string Outcome, string Evidence);

internal sealed record NativeValidationRecoveryCandidate(
    string EntryName,
    string Category,
    string Outcome,
    string? SourcePath,
    string? QuarantinePath,
    NativeValidationFileIdentity? Identity,
    string Evidence);
