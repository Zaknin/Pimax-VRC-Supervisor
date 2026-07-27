using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class VerifiedPackageDownloadTests
{
    [Fact]
    public async Task VerifiedExactPackageStreamsToVersionedStagingAndPersistsEvidence()
    {
        using var temp = new TempDirectory();
        var bytes = Enumerable.Range(0, 32_768).Select(index => (byte)(index % 251)).ToArray();
        var manifest = UpdateContractTestData.CreateManifest();
        var package = manifest["assets"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["variant"]!.GetValue<string>() == "with-dotnet9");
        package["sizeBytes"] = bytes.LongLength;
        package["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var signed = UpdateContractTestData.Sign(manifest);
        var scenario = new DiscoveryScenario(signed);
        var transport = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.ReleaseMetadata),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signed.ManifestBytes),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signed.SignatureEnvelopeBytes),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes, setContentLength: false));
        var discovery = TestOnlyDiscoveryHarness.Create(
            transport,
            signed.TrustStore,
            new UpdateDiscoveryOptions
            {
                InstalledVersion = SemanticVersion.Parse("1.3.1"),
                InstalledVariant = UpdatePackageVariant.WithDotnet9
            });
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var downloader = new VerifiedPackageDownloader(
            discovery,
            transport,
            store,
            new VerifiedPackageDownloadOptions
            {
                HardMaximumPackageBytes = 64 * 1024,
                RequestTimeout = TimeSpan.FromSeconds(2),
                IdleTimeout = TimeSpan.FromSeconds(2)
            },
            new ManualUpdateScheduleClock(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero)),
            new UpdateCheckAdmission(new IsolatedUpdateCheckExclusion()));

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.True(result.CompletedSuccessfully, result.ResultCode);
        Assert.Equal("downloaded_and_verified", result.ResultCode);
        Assert.NotNull(result.Record);
        Assert.Equal(bytes.LongLength, result.Record!.ActualSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Record.ActualSha256);
        Assert.Equal("downloadedAndVerified", result.Record.State);
        Assert.True(File.Exists(store.GetPackagePath(result.Record)));
        Assert.True(File.Exists(store.GetRecordPath(result.Record)));
        var evidenceDirectory = Path.GetDirectoryName(store.GetRecordPath(result.Record))!;
        Assert.Equal(signed.ManifestBytes, File.ReadAllBytes(Path.Combine(evidenceDirectory, VerifiedPackageStore.ReleaseManifestFileName)));
        Assert.Equal(signed.SignatureEnvelopeBytes, File.ReadAllBytes(Path.Combine(evidenceDirectory, VerifiedPackageStore.ReleaseSignatureFileName)));
        Assert.True(File.Exists(Path.Combine(evidenceDirectory, VerifiedPackageStore.ReleaseEvidenceFileName)));
        Assert.DoesNotContain(Directory.EnumerateFiles(temp.Path, "*.partial", SearchOption.AllDirectories), _ => true);
        Assert.Equal(4, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.Null(request.Authorization));
        Assert.EndsWith(result.Record.Filename, transport.Requests[^1].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentLengthMismatchIsRejectedBeforePackageBodyRead()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var content = new TrackingHttpContent([1], declaredLength: 5);
        var transport = new FakeUpdateHttpTransport(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("package_content_length", result.ResultCode);
        Assert.False(content.ReadStarted);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExcessStreamingBytesAreRejectedAndNeverPromoted()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, [1, 2, 3, 4, 5], setContentLength: false));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("package_too_large", result.ResultCode);
        Assert.False(File.Exists(Path.Combine(temp.Path, authority.FileName)));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task BusyAdmissionMakesNoNetworkRequestOrStateMutation()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var transport = new FakeUpdateHttpTransport(_ => throw new InvalidOperationException("must not request"));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport, new UpdateCheckAdmission(new BusyExclusion()));

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("already_running", result.ResultCode);
        Assert.Empty(transport.Requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task DigestMismatchDeletesPartialAndCreatesNoVerifiedRecord()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, [4, 3, 2, 1]));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("package_digest_mismatch", result.ResultCode);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, VerifiedPackageStore.RecordFileName, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReopenedPackageDigestMismatchFailsClosed()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await File.WriteAllBytesAsync(store.GetPackagePath(result.Record!), [4, 3, 2, 1]);

        var reloaded = store.Load(authority.Version, authority.Variant, authority.FileName);

        Assert.True(result.CompletedSuccessfully);
        Assert.Null(reloaded.Record);
        Assert.True(reloaded.CorruptionDetected);
    }

    [Fact]
    public void PackageWithoutVerifiedRecordFailsClosed()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var packagePath = store.GetFinalPath(authority.Capability);
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var loaded = store.Load(authority.Version, authority.Variant, authority.FileName);

        Assert.Null(loaded.Record);
        Assert.True(loaded.CorruptionDetected);
    }

    [Fact]
    public async Task PathOnlyLoadNeverTreatsUserWritablePackageEvidenceAsInstallEligible()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes));
        var result = await CreateDirectDownloader(temp.Path, authority, transport).DownloadAsync(CancellationToken.None);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);

        var loaded = store.Load(authority.Version, authority.Variant, authority.FileName);

        Assert.True(result.CompletedSuccessfully);
        Assert.Null(loaded.Record);
        Assert.False(loaded.CorruptionDetected);
    }

    [Fact]
    public async Task RecoveryRejectsRecordWithContradictoryLocalPackageRelativePath()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes));
        var downloaded = await CreateDirectDownloader(temp.Path, authority, transport).DownloadAsync(CancellationToken.None);
        Assert.NotNull(downloaded.Record);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var recordPath = store.GetRecordPath(downloaded.Record!);
        var record = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        record["localPackageRelativePath"] = "Packages\\unrelated.zip";
        File.WriteAllText(recordPath, record.ToJsonString());

        var exception = await Assert.ThrowsAsync<UpdateContractException>(() => store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero), CancellationToken.None));

        Assert.Equal("package_record", exception.Code);
        Assert.True(File.Exists(recordPath));
    }

    [Theory]
    [InlineData(VerifiedPackageStore.ReleaseManifestFileName)]
    [InlineData(VerifiedPackageStore.ReleaseSignatureFileName)]
    public async Task RecoveryRejectsModifiedExactSignedEvidence(string evidenceName)
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var downloaded = await CreateDirectDownloader(temp.Path, authority, new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes))).DownloadAsync(CancellationToken.None);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var evidencePath = Path.Combine(Path.GetDirectoryName(store.GetRecordPath(downloaded.Record!))!, evidenceName);
        var evidence = File.ReadAllBytes(evidencePath);
        evidence[^1] ^= 0x01;
        File.WriteAllBytes(evidencePath, evidence);

        var exception = await Assert.ThrowsAsync<UpdateContractException>(() => store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero), CancellationToken.None));

        Assert.Equal("package_evidence", exception.Code);
        Assert.True(File.Exists(evidencePath));
    }

    [Theory]
    [InlineData(VerifiedPackageStore.ReleaseEvidenceFileName)]
    [InlineData(VerifiedPackageStore.ReleaseManifestFileName)]
    [InlineData(VerifiedPackageStore.ReleaseSignatureFileName)]
    public async Task RecoveryUsesThePreviousCompleteSignedEvidenceGenerationWhenCurrentEvidenceIsCorrupt(string evidenceName)
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var downloaded = await CreateDirectDownloader(
            temp.Path,
            authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes)),
            store: store).DownloadAsync(CancellationToken.None);
        Assert.True(downloaded.CompletedSuccessfully, downloaded.ResultCode);

        using (var directory = store.OpenDirectory(authority.Capability))
        using (var package = directory.OpenExistingFile(authority.FileName, write: false))
        {
            await store.PersistAsync(
                "renewal-" + Guid.NewGuid().ToString("N"),
                authority.Capability,
                directory,
                package,
                authority.ExpectedSize,
                authority.ExpectedSha256,
                new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero),
                CancellationToken.None);
        }

        var packageDirectory = Path.GetDirectoryName(store.GetRecordPath(downloaded.Record!))!;
        Assert.True(File.Exists(Path.Combine(packageDirectory, VerifiedPackageStore.PreviousReleaseEvidenceFileName)));
        var currentEvidencePath = Path.Combine(packageDirectory, evidenceName);
        File.WriteAllBytes(currentEvidencePath, [0x01]);

        var recovered = await store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 2, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(authority.ExpectedSha256, recovered!.ActualSha256);
    }

    [Fact]
    public void DiscoveryExposesNoCallableAuthorityConstructor()
    {
        Assert.Empty(typeof(GitHubUpdateDiscoveryClient).GetConstructors());
        Assert.DoesNotContain(
            typeof(GitHubUpdateDiscoveryClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            method => method.Name.Contains("Mint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallerCreatedAuthorityViewIsRejectedBeforeNetworkOrFilesystemMutation()
    {
        using var temp = new TempDirectory();
        var genuine = CreateAuthority([1, 2, 3, 4]);
        var forged = genuine.View with { ExpectedSha256 = new string('0', 64) };
        var transport = new FakeUpdateHttpTransport(_ => throw new InvalidOperationException("must not request"));
        var downloader = CreateDirectDownloader(temp.Path, forged, genuine.View, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("package_not_verified", result.ResultCode);
        Assert.Empty(transport.Requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));

        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        Assert.Throws<UpdateContractException>(() => store.GetFinalPath(forged));
        Assert.Throws<UpdateContractException>(() => store.OpenDirectory(forged));
        await Assert.ThrowsAsync<UpdateContractException>(() => store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            forged,
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None));
        await Assert.ThrowsAsync<UpdateContractException>(() => store.WriteJournalAsync(
            "download-" + Guid.NewGuid().ToString("N"),
            forged,
            directory: null!,
            VerifiedPackageJournalState.PartialCreated,
            genuine.FileName + ".partial",
            partialIdentity: null,
            finalIdentity: null,
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None));
        await Assert.ThrowsAsync<UpdateContractException>(() => store.PersistAsync(
            "download-" + Guid.NewGuid().ToString("N"),
            forged,
            directory: null!,
            promotedPackage: null!,
            genuine.ExpectedSize,
            genuine.ExpectedSha256,
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task ArbitraryAuthorityObjectIsRejectedBeforeNetworkOrFilesystemMutation()
    {
        using var temp = new TempDirectory();
        var genuine = CreateAuthority([1, 2, 3, 4]);
        var transport = new FakeUpdateHttpTransport(_ => throw new InvalidOperationException("must not request"));
        var downloader = CreateDirectDownloader(temp.Path, new object(), genuine.View, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("package_not_verified", result.ResultCode);
        Assert.Empty(transport.Requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task GitHubReleaseAssetRedirectIsAllowedAndDoesNotExposeAuthorization()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var redirected = new Uri("https://release-assets.githubusercontent.com/asset/package?opaque=transient");
        var transport = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Redirect(redirected),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.True(result.CompletedSuccessfully, result.ResultCode);
        Assert.Equal(redirected, transport.Requests[1].Uri);
        Assert.All(transport.Requests, request => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task ArbitraryPackageRedirectIsRejectedBeforeRequestingIt()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Redirect(new Uri("https://example.invalid/package.zip")));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("redirect_rejected", result.ResultCode);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task InstalledVariantMismatchIsRejectedBeforeNetworkRequest()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4], UpdatePackageVariant.NoDotnet9);
        var transport = new FakeUpdateHttpTransport(_ => throw new InvalidOperationException("must not request"));
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("package_variant_mismatch", result.ResultCode);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void VerifierAuthorityCarriesOnlyTheManifestBoundedSize()
    {
        Assert.InRange(CreateAuthority([1, 2, 3, 4]).ExpectedSize, 1, UpdateManifestConstants.MaximumPackageBytes);
    }

    [Fact]
    public async Task CancellationRemovesPartialAndNeverCreatesRecord()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var transport = new FakeUpdateHttpTransport(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        var downloader = CreateDirectDownloader(temp.Path, authority, transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        var result = await downloader.DownloadAsync(cancellation.Token);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("cancelled", result.ResultCode);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, VerifiedPackageStore.RecordFileName, SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(true, (int)VerifiedPackageJournalState.Cancelled)]
    [InlineData(false, (int)VerifiedPackageJournalState.Failed)]
    public async Task CancellationAndDigestFailureRemainDurablyTerminalUntilFreshRecovery(
        bool cancel,
        int expectedState)
    {
        using var temp = new TempDirectory();
        var expected = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(expected);
        using var cancellation = new CancellationTokenSource();
        var transport = cancel
            ? new FakeUpdateHttpTransport(async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return FakeUpdateHttpTransport.Response(HttpStatusCode.OK, expected);
            })
            : new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, [4, 3, 2, 1]));
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var downloader = CreateDirectDownloader(temp.Path, authority, transport, store: store);

        var result = await downloader.DownloadAsync(cancel ? cancellation.Token : CancellationToken.None);
        var journalPath = Path.Combine(
            Path.GetDirectoryName(store.GetFinalPath(authority.Capability))!,
            VerifiedPackageStore.JournalFileName);

        Assert.False(result.CompletedSuccessfully);
        Assert.True(File.Exists(journalPath));
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
        Assert.Equal(expectedState, journal["state"]!.GetValue<int>());
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.partial", SearchOption.AllDirectories));

        var recovered = await store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Null(recovered);
        Assert.False(File.Exists(journalPath));
    }

    [Theory]
    [InlineData((int)UpdateDownloadFaultPoint.AfterInitialJournalDurability, false)]
    [InlineData((int)UpdateDownloadFaultPoint.AfterNativePromotion, true)]
    [InlineData((int)UpdateDownloadFaultPoint.AfterFinalRecordDurabilityBeforeJournalRetirement, true)]
    public async Task RecoveryPreservesIdentityMismatchedReplacementAndJournal(
        int faultPointValue,
        bool promoted)
    {
        using var temp = new TempDirectory();
        var expected = new byte[] { 1, 2, 3, 4 };
        var replacement = new byte[] { 9, 8, 7, 6 };
        var authority = CreateAuthority(expected);
        var store = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            new ThrowAtFaultPoint((UpdateDownloadFaultPoint)faultPointValue));
        var downloader = CreateDirectDownloader(
            temp.Path,
            authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, expected)),
            store: store);

        var interrupted = await downloader.DownloadAsync(CancellationToken.None);
        var finalPath = store.GetFinalPath(authority.Capability);
        var journalPath = Path.Combine(Path.GetDirectoryName(finalPath)!, VerifiedPackageStore.JournalFileName);
        var replacementPath = promoted ? finalPath : finalPath + ".partial";
        if (File.Exists(replacementPath))
        {
            File.Delete(replacementPath);
        }
        File.WriteAllBytes(replacementPath, replacement);

        var exception = await Assert.ThrowsAsync<UpdateContractException>(() => store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero),
            CancellationToken.None));

        Assert.False(interrupted.CompletedSuccessfully);
        Assert.Equal("package_journal", exception.Code);
        Assert.Equal(replacement, File.ReadAllBytes(replacementPath));
        Assert.True(File.Exists(journalPath));
    }

    [Theory]
    [InlineData("partial-verified")]
    [InlineData("invalid-promoted-final")]
    [InlineData("cancelled-partial")]
    [InlineData("failed-partial")]
    [InlineData("cancelled-final")]
    [InlineData("failed-final")]
    public async Task RecoveryDeletionRemainsBoundToVerifiedHandleWhenPathIsReplaced(string scenario)
    {
        using var temp = new TempDirectory();
        var expected = new byte[] { 1, 2, 3, 4 };
        var original = scenario == "partial-verified" ? expected : new byte[] { 9, 8, 7, 6 };
        var replacement = new byte[] { 5, 5, 5, 5 };
        var authority = CreateAuthority(expected);
        var preparationStore = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var operationId = "download-" + Guid.NewGuid().ToString("N");
        var timestamp = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var partialName = authority.FileName + ".partial";
        var finalScenario = scenario is "invalid-promoted-final" or "cancelled-final" or "failed-final";

        using (var directory = preparationStore.OpenDirectory(authority.Capability))
        using (var partial = directory.CreateNewFile(partialName, write: true))
        {
            RandomAccess.Write(partial, original, 0);
            directory.FlushPinnedFile(partial);
            var partialIdentity = directory.CaptureFileIdentity(partial);
            await preparationStore.WriteJournalAsync(
                operationId, authority.Capability, directory,
                VerifiedPackageJournalState.PartialCreated, partialName, partialIdentity, null,
                timestamp, CancellationToken.None);

            if (scenario == "cancelled-partial" || scenario == "failed-partial")
            {
                var terminalState = scenario == "cancelled-partial"
                    ? VerifiedPackageJournalState.Cancelled
                    : VerifiedPackageJournalState.Failed;
                await preparationStore.WriteJournalAsync(
                    operationId, authority.Capability, directory,
                    terminalState, partialName, partialIdentity, null,
                    timestamp.AddMinutes(1), CancellationToken.None);
            }
            else
            {
                await preparationStore.WriteJournalAsync(
                    operationId, authority.Capability, directory,
                    VerifiedPackageJournalState.Downloading, partialName, partialIdentity, null,
                    timestamp.AddMinutes(1), CancellationToken.None);
                await preparationStore.WriteJournalAsync(
                    operationId, authority.Capability, directory,
                    VerifiedPackageJournalState.PartialVerified, partialName, partialIdentity, null,
                    timestamp.AddMinutes(2), CancellationToken.None);

                if (finalScenario)
                {
                    directory.RenamePinnedFile(partial, authority.FileName);
                    var finalIdentity = directory.CaptureFileIdentity(partial);
                    await preparationStore.WriteJournalAsync(
                        operationId, authority.Capability, directory,
                        VerifiedPackageJournalState.PromotedPendingRecord, null, null, finalIdentity,
                        timestamp.AddMinutes(3), CancellationToken.None);
                    if (scenario == "cancelled-final" || scenario == "failed-final")
                    {
                        var terminalState = scenario == "cancelled-final"
                            ? VerifiedPackageJournalState.Cancelled
                            : VerifiedPackageJournalState.Failed;
                        await preparationStore.WriteJournalAsync(
                            operationId, authority.Capability, directory,
                            terminalState, null, null, finalIdentity,
                            timestamp.AddMinutes(4), CancellationToken.None);
                    }
                }
            }
        }

        var finalPath = preparationStore.GetFinalPath(authority.Capability);
        var victimPath = finalScenario ? finalPath : finalPath + ".partial";
        var originalPath = victimPath + ".verified-original";
        var unrelatedPath = Path.Combine(Path.GetDirectoryName(victimPath)!, "unrelated.bin");
        var journalPath = Path.Combine(Path.GetDirectoryName(victimPath)!, VerifiedPackageStore.JournalFileName);
        File.WriteAllBytes(unrelatedPath, [7, 7, 7]);
        var swapCount = 0;
        var recoveryStore = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            beforeRecoveryDeleteForTests: name =>
            {
                if (!string.Equals(name, Path.GetFileName(victimPath), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The recovery deletion seam targeted an unexpected file.");
                }
                File.Move(victimPath, originalPath);
                File.WriteAllBytes(victimPath, replacement);
                swapCount++;
            });

        var exception = await Record.ExceptionAsync(() => recoveryStore.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            timestamp.AddMinutes(5), CancellationToken.None));

        Assert.NotNull(exception);
        Assert.True(exception is UpdateContractException or IOException, exception.ToString());
        if (exception is IOException)
        {
            Assert.Equal(0, swapCount);
            Assert.Equal(original, File.ReadAllBytes(victimPath));
            Assert.False(File.Exists(originalPath));
        }
        else
        {
            Assert.Equal(1, swapCount);
            Assert.Equal(replacement, File.ReadAllBytes(victimPath));
            Assert.Equal(original, File.ReadAllBytes(originalPath));
        }
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(unrelatedPath));
        Assert.True(File.Exists(journalPath));

        if (exception is IOException)
        {
            return;
        }

        var repeated = await Assert.ThrowsAsync<UpdateContractException>(() => preparationStore.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            timestamp.AddMinutes(6), CancellationToken.None));
        Assert.Equal("package_journal", repeated.Code);
        Assert.Equal(replacement, File.ReadAllBytes(victimPath));
        Assert.Equal(original, File.ReadAllBytes(originalPath));
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(unrelatedPath));
        Assert.True(File.Exists(journalPath));
    }

    [Fact]
    public async Task RecoveryJournalRetirementRemainsBoundToVerifiedHandleWhenPathIsReplaced()
    {
        using var temp = new TempDirectory();
        var expected = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(expected);
        var preparationStore = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var operationId = "download-" + Guid.NewGuid().ToString("N");
        var timestamp = new DateTimeOffset(2026, 7, 22, 13, 0, 0, TimeSpan.Zero);
        var partialName = authority.FileName + ".partial";

        using (var directory = preparationStore.OpenDirectory(authority.Capability))
        using (var partial = directory.CreateNewFile(partialName, write: true))
        {
            RandomAccess.Write(partial, expected, 0);
            directory.FlushPinnedFile(partial);
            var partialIdentity = directory.CaptureFileIdentity(partial);
            await preparationStore.WriteJournalAsync(
                operationId, authority.Capability, directory,
                VerifiedPackageJournalState.PartialCreated, partialName, partialIdentity, null,
                timestamp, CancellationToken.None);
            await preparationStore.WriteJournalAsync(
                operationId, authority.Capability, directory,
                VerifiedPackageJournalState.Downloading, partialName, partialIdentity, null,
                timestamp.AddMinutes(1), CancellationToken.None);
            await preparationStore.WriteJournalAsync(
                operationId, authority.Capability, directory,
                VerifiedPackageJournalState.PartialVerified, partialName, partialIdentity, null,
                timestamp.AddMinutes(2), CancellationToken.None);
            directory.RenamePinnedFile(partial, authority.FileName);
            var finalIdentity = directory.CaptureFileIdentity(partial);
            await preparationStore.WriteJournalAsync(
                operationId, authority.Capability, directory,
                VerifiedPackageJournalState.PromotedPendingRecord, null, null, finalIdentity,
                timestamp.AddMinutes(3), CancellationToken.None);
        }

        var finalPath = preparationStore.GetFinalPath(authority.Capability);
        var packageDirectory = Path.GetDirectoryName(finalPath)!;
        var journalPath = Path.Combine(packageDirectory, VerifiedPackageStore.JournalFileName);
        var originalJournalPath = journalPath + ".verified-original";
        var unrelatedPath = Path.Combine(packageDirectory, "unrelated.bin");
        File.WriteAllBytes(unrelatedPath, [7, 7, 7]);
        var swapCount = 0;
        var recoveryStore = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            beforeRecoveryDeleteForTests: name =>
            {
                if (string.Equals(name, VerifiedPackageStore.PreviousJournalFileName, StringComparison.Ordinal))
                {
                    return;
                }
                if (!string.Equals(name, VerifiedPackageStore.JournalFileName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The journal retirement seam targeted an unexpected file.");
                }
                File.Move(journalPath, originalJournalPath);
                File.WriteAllText(journalPath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                swapCount++;
            });

        var exception = await Record.ExceptionAsync(() => recoveryStore.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            timestamp.AddMinutes(4), CancellationToken.None));

        Assert.NotNull(exception);
        Assert.True(exception is UpdateContractException or IOException, exception.ToString());
        if (exception is IOException)
        {
            Assert.Equal(0, swapCount);
            Assert.NotEqual("{}", File.ReadAllText(journalPath));
            Assert.False(File.Exists(originalJournalPath));
        }
        else
        {
            Assert.Equal(1, swapCount);
            Assert.Equal("{}", File.ReadAllText(journalPath));
            Assert.True(File.Exists(originalJournalPath));
        }
        Assert.Equal(expected, File.ReadAllBytes(finalPath));
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(unrelatedPath));

        if (exception is IOException)
        {
            return;
        }

        var repeated = await Record.ExceptionAsync(() => preparationStore.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            timestamp.AddMinutes(5), CancellationToken.None));
        Assert.True(repeated is UpdateContractException or JsonException, repeated?.ToString());
        Assert.Equal("{}", File.ReadAllText(journalPath));
        Assert.True(File.Exists(originalJournalPath));
        Assert.Equal(expected, File.ReadAllBytes(finalPath));
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(unrelatedPath));
    }

    [Fact]
    public async Task RecoveryRecordRetirementRemainsBoundToVerifiedHandleWhenPathIsReplaced()
    {
        using var temp = new TempDirectory();
        var expected = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(expected);
        var transport = new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, expected));
        var downloaded = await CreateDirectDownloader(temp.Path, authority, transport).DownloadAsync(CancellationToken.None);
        Assert.True(downloaded.CompletedSuccessfully);

        var preparationStore = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var finalPath = preparationStore.GetFinalPath(authority.Capability);
        var packageDirectory = Path.GetDirectoryName(finalPath)!;
        var recordPath = Path.Combine(packageDirectory, VerifiedPackageStore.RecordFileName);
        var originalRecordPath = recordPath + ".verified-original";
        var unrelatedPath = Path.Combine(packageDirectory, "unrelated.bin");
        var tamperedPackage = new byte[] { 4, 3, 2, 1 };
        File.WriteAllBytes(finalPath, tamperedPackage);
        File.WriteAllBytes(unrelatedPath, [7, 7, 7]);
        var swapCount = 0;
        var recoveryStore = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            beforeRecoveryDeleteForTests: name =>
            {
                if (!string.Equals(name, VerifiedPackageStore.RecordFileName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The record retirement seam targeted an unexpected file.");
                }
                File.Move(recordPath, originalRecordPath);
                File.WriteAllText(recordPath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                swapCount++;
            });

        var exception = await Record.ExceptionAsync(() => recoveryStore.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            new DateTimeOffset(2026, 7, 22, 14, 0, 0, TimeSpan.Zero), CancellationToken.None));

        Assert.NotNull(exception);
        Assert.True(exception is UpdateContractException or IOException, exception.ToString());
        if (exception is IOException)
        {
            Assert.Equal(0, swapCount);
            Assert.NotEqual("{}", File.ReadAllText(recordPath));
            Assert.False(File.Exists(originalRecordPath));
        }
        else
        {
            Assert.Equal(1, swapCount);
            Assert.Equal("{}", File.ReadAllText(recordPath));
            Assert.True(File.Exists(originalRecordPath));
        }
        Assert.Equal(tamperedPackage, File.ReadAllBytes(finalPath));
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(unrelatedPath));

        if (exception is IOException)
        {
            return;
        }

        var repeated = await Record.ExceptionAsync(() => preparationStore.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            new DateTimeOffset(2026, 7, 22, 14, 1, 0, TimeSpan.Zero), CancellationToken.None));
        Assert.True(repeated is UpdateContractException or JsonException, repeated?.ToString());
        Assert.Equal("{}", File.ReadAllText(recordPath));
        Assert.True(File.Exists(originalRecordPath));
        Assert.Equal(tamperedPackage, File.ReadAllBytes(finalPath));
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(unrelatedPath));
    }

    [Fact]
    public async Task FreshVerifierAuthorityRehashesPromotedPackageAndRecreatesMissingRecord()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var downloader = CreateDirectDownloader(temp.Path, authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes)));

        var downloaded = await downloader.DownloadAsync(CancellationToken.None);
        Assert.True(downloaded.CompletedSuccessfully, downloaded.ResultCode);
        File.Delete(new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path).GetRecordPath(downloaded.Record!));

        var recovered = await new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path).RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), recovered!.ActualSha256);
        Assert.False(new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path).Load(authority.Version, authority.Variant, authority.FileName).CorruptionDetected);
    }

    [Fact]
    public async Task RecoveryUsesThePreviousDurableRecordWhenTheCurrentRecordIsCorrupt()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var downloaded = await CreateDirectDownloader(
            temp.Path,
            authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes)),
            store: store).DownloadAsync(CancellationToken.None);
        Assert.True(downloaded.CompletedSuccessfully, downloaded.ResultCode);

        using (var directory = store.OpenDirectory(authority.Capability))
        using (var package = directory.OpenExistingFile(authority.FileName, write: false))
        {
            await store.PersistAsync(
                "renewal-" + Guid.NewGuid().ToString("N"),
                authority.Capability,
                directory,
                package,
                authority.ExpectedSize,
                authority.ExpectedSha256,
                new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero),
                CancellationToken.None);
        }

        var recordPath = store.GetRecordPath(downloaded.Record!);
        var previousRecordPath = Path.Combine(Path.GetDirectoryName(recordPath)!, VerifiedPackageStore.PreviousRecordFileName);
        Assert.True(File.Exists(previousRecordPath));
        File.WriteAllText(recordPath, "{}");

        var recovered = await store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 2, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(authority.ExpectedSha256, recovered!.ActualSha256);
        Assert.True(File.Exists(previousRecordPath));
    }

    [Fact]
    public async Task RecoveryUsesThePreviousDurableJournalWhenTheCurrentJournalIsCorrupt()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var operationId = "journal-" + Guid.NewGuid().ToString("N");
        var partialName = authority.FileName + ".partial";
        var updatedAt = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        NativeFileIdentity partialIdentity;

        using (var directory = store.OpenDirectory(authority.Capability))
        using (var partial = directory.CreateNewFile(partialName, write: true))
        {
            RandomAccess.Write(partial, [1, 2, 3, 4], 0);
            directory.FlushPinnedFile(partial);
            partialIdentity = directory.CaptureFileIdentity(partial);
            await store.WriteJournalAsync(
                operationId,
                authority.Capability,
                directory,
                VerifiedPackageJournalState.PartialCreated,
                partialName,
                partialIdentity,
                null,
                updatedAt,
                CancellationToken.None);
            await store.WriteJournalAsync(
                operationId,
                authority.Capability,
                directory,
                VerifiedPackageJournalState.Downloading,
                partialName,
                partialIdentity,
                null,
                updatedAt.AddSeconds(1),
                CancellationToken.None);
        }

        var packageDirectory = Path.GetDirectoryName(store.GetFinalPath(authority.Capability))!;
        var currentJournalPath = Path.Combine(packageDirectory, VerifiedPackageStore.JournalFileName);
        var previousJournalPath = Path.Combine(packageDirectory, VerifiedPackageStore.PreviousJournalFileName);
        Assert.True(File.Exists(previousJournalPath));
        File.WriteAllText(currentJournalPath, "{}");

        var recovered = await store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            updatedAt.AddMinutes(1),
            CancellationToken.None);

        Assert.Null(recovered);
        Assert.False(File.Exists(Path.Combine(packageDirectory, partialName)));
        Assert.False(File.Exists(currentJournalPath));
        Assert.False(File.Exists(previousJournalPath));
    }

    [Theory]
    [InlineData((int)UpdateDownloadFaultPoint.AfterInitialJournalDurability, false)]
    [InlineData((int)UpdateDownloadFaultPoint.DuringDownloading, false)]
    [InlineData((int)UpdateDownloadFaultPoint.AfterPartialVerificationJournalDurability, false)]
    [InlineData((int)UpdateDownloadFaultPoint.BeforePromotion, false)]
    [InlineData((int)UpdateDownloadFaultPoint.AfterNativePromotion, true)]
    [InlineData((int)UpdateDownloadFaultPoint.BeforeFinalRecordWrite, true)]
    [InlineData((int)UpdateDownloadFaultPoint.DuringFinalRecordWrite, true)]
    [InlineData((int)UpdateDownloadFaultPoint.AfterFinalRecordDurabilityBeforeJournalRetirement, true)]
    public async Task DurableJournalRecoversEveryInjectedTransactionBoundary(
        int faultPointValue,
        bool recoveryCompletes)
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var faultPoint = (UpdateDownloadFaultPoint)faultPointValue;
        var faultedStore = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            new ThrowAtFaultPoint(faultPoint));
        var downloader = CreateDirectDownloader(
            temp.Path,
            authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes)),
            store: faultedStore);

        var interrupted = await downloader.DownloadAsync(CancellationToken.None);
        var journalPath = Path.Combine(
            Path.GetDirectoryName(faultedStore.GetFinalPath(authority.Capability))!,
            VerifiedPackageStore.JournalFileName);

        Assert.False(interrupted.CompletedSuccessfully);
        Assert.Equal("package_io", interrupted.ResultCode);
        Assert.True(File.Exists(journalPath));

        var recovered = await new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path).RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"),
            authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(recoveryCompletes, recovered is not null);
        Assert.False(File.Exists(journalPath));
        if (recoveryCompletes)
        {
            Assert.Equal(authority.ExpectedSize, recovered!.ActualSize);
            Assert.False(new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path)
                .Load(authority.Version, authority.Variant, authority.FileName).CorruptionDetected);
        }
        else
        {
            Assert.False(File.Exists(faultedStore.GetFinalPath(authority.Capability)));
        }
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unknown-schema")]
    [InlineData("unknown-state")]
    [InlineData("authority-mismatch")]
    [InlineData("unsafe-partial")]
    [InlineData("contradictory-final")]
    [InlineData("future-time")]
    [InlineData("stale-time")]
    [InlineData("unknown-property")]
    public async Task MalformedOrAuthorityMismatchedJournalIsRejectedAndPreserved(string mutation)
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        string journalPath;
        using (var directory = store.OpenDirectory(authority.Capability))
        using (var partial = directory.CreateNewFile(authority.FileName + ".partial", write: true))
        {
            var identity = directory.CaptureFileIdentity(partial);
            await store.WriteJournalAsync(
                "download-" + Guid.NewGuid().ToString("N"), authority.Capability, directory,
                VerifiedPackageJournalState.PartialCreated, authority.FileName + ".partial", identity, null,
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);
            journalPath = Path.Combine(Path.GetDirectoryName(store.GetFinalPath(authority.Capability))!, VerifiedPackageStore.JournalFileName);
        }

        if (mutation == "malformed")
        {
            File.WriteAllText(journalPath, "{");
        }
        else
        {
            var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
            switch (mutation)
            {
                case "unknown-schema": journal["schemaVersion"] = 2; break;
                case "unknown-state": journal["state"] = 999; break;
                case "authority-mismatch": journal["expectedSha256"] = new string('0', 64); break;
                case "unsafe-partial": journal["partialFilename"] = "..\\escape.partial"; break;
                case "contradictory-final": journal["finalFilename"] = authority.FileName; break;
                case "future-time": journal["updatedAt"] = "2030-01-01T00:00:00.0000000Z"; break;
                case "stale-time": journal["updatedAt"] = "2026-06-01T00:00:00.0000000Z"; break;
                case "unknown-property": journal["unexpected"] = true; break;
            }
            File.WriteAllText(journalPath, journal.ToJsonString());
        }

        var exception = await Record.ExceptionAsync(() => store.RecoverAsync(
            "recovery-" + Guid.NewGuid().ToString("N"), authority.Capability,
            new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero), CancellationToken.None));

        Assert.NotNull(exception);
        Assert.True(exception is UpdateContractException or JsonException, exception.ToString());
        Assert.True(File.Exists(journalPath));
    }

    [Fact]
    public async Task IllegalJournalTransitionIsRejectedWithoutReplacingDurableState()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        using var directory = store.OpenDirectory(authority.Capability);
        using var partial = directory.CreateNewFile(authority.FileName + ".partial", write: true);
        var identity = directory.CaptureFileIdentity(partial);
        var operationId = "download-" + Guid.NewGuid().ToString("N");
        await store.WriteJournalAsync(operationId, authority.Capability, directory,
            VerifiedPackageJournalState.PartialCreated, authority.FileName + ".partial", identity, null,
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<UpdateContractException>(() => store.WriteJournalAsync(
            operationId, authority.Capability, directory,
            VerifiedPackageJournalState.PartialVerified, authority.FileName + ".partial", identity, null,
            new DateTimeOffset(2026, 7, 22, 12, 1, 0, TimeSpan.Zero), CancellationToken.None));

        Assert.Equal("package_journal_transition", exception.Code);
        var journalPath = Path.Combine(Path.GetDirectoryName(store.GetFinalPath(authority.Capability))!, VerifiedPackageStore.JournalFileName);
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
        Assert.Equal((int)VerifiedPackageJournalState.PartialCreated, journal["state"]!.GetValue<int>());
    }

    [Fact]
    public async Task JournalTransitionRejectsBackwardTimestampAndChangedOwnedIdentity()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var store = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var operationId = "download-" + Guid.NewGuid().ToString("N");
        var initial = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        using var directory = store.OpenDirectory(authority.Capability);
        using var partial = directory.CreateNewFile(authority.FileName + ".partial", write: true);
        using var replacement = directory.CreateNewFile("replacement.bin", write: true);
        var partialIdentity = directory.CaptureFileIdentity(partial);
        var replacementIdentity = directory.CaptureFileIdentity(replacement);
        await store.WriteJournalAsync(operationId, authority.Capability, directory, VerifiedPackageJournalState.PartialCreated,
            authority.FileName + ".partial", partialIdentity, null, initial, CancellationToken.None);

        var backward = await Assert.ThrowsAsync<UpdateContractException>(() => store.WriteJournalAsync(
            operationId, authority.Capability, directory, VerifiedPackageJournalState.Downloading,
            authority.FileName + ".partial", partialIdentity, null, initial - TimeSpan.FromSeconds(1), CancellationToken.None));
        var changedIdentity = await Assert.ThrowsAsync<UpdateContractException>(() => store.WriteJournalAsync(
            operationId, authority.Capability, directory, VerifiedPackageJournalState.Downloading,
            authority.FileName + ".partial", replacementIdentity, null, initial + TimeSpan.FromSeconds(1), CancellationToken.None));

        Assert.Equal("package_journal_transition", backward.Code);
        Assert.Equal("package_journal_transition", changedIdentity.Code);
    }

    [Theory]
    [InlineData("expected-zero")]
    [InlineData("actual-zero")]
    [InlineData("size-mismatch")]
    [InlineData("expected-over-maximum")]
    [InlineData("actual-over-maximum")]
    [InlineData("malformed-expected-digest")]
    [InlineData("malformed-actual-digest")]
    public async Task ImpossibleRecordEvidenceIsRejectedBeforeHashingStarts(string mutation)
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var downloaded = await CreateDirectDownloader(
            temp.Path,
            authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes)))
            .DownloadAsync(CancellationToken.None);
        Assert.True(downloaded.CompletedSuccessfully, downloaded.ResultCode);
        var recordPath = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path).GetRecordPath(downloaded.Record!);
        var record = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        switch (mutation)
        {
            case "expected-zero": record["expectedSize"] = 0; break;
            case "actual-zero": record["actualSize"] = 0; break;
            case "size-mismatch": record["actualSize"] = bytes.LongLength + 1; break;
            case "expected-over-maximum": record["expectedSize"] = UpdateManifestConstants.MaximumPackageBytes + 1; break;
            case "actual-over-maximum": record["actualSize"] = UpdateManifestConstants.MaximumPackageBytes + 1; break;
            case "malformed-expected-digest": record["expectedSha256"] = "not-a-digest"; break;
            case "malformed-actual-digest": record["actualSha256"] = "not-a-digest"; break;
        }
        File.WriteAllText(recordPath, record.ToJsonString());
        var hashStarts = 0;

        var loaded = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            hashStartedForTests: () => hashStarts++)
            .Load(authority.Version, authority.Variant, authority.FileName);

        Assert.Null(loaded.Record);
        Assert.True(loaded.CorruptionDetected);
        Assert.Equal(0, hashStarts);
    }

    [Fact]
    public async Task DiskFileAboveGlobalMaximumIsRejectedBeforeHashingStarts()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var downloaded = await CreateDirectDownloader(
            temp.Path,
            authority,
            new FakeUpdateHttpTransport(_ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes)))
            .DownloadAsync(CancellationToken.None);
        Assert.True(downloaded.CompletedSuccessfully, downloaded.ResultCode);
        var packagePath = new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, temp.Path).GetPackagePath(downloaded.Record!);
        using (var package = new FileStream(packagePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            package.SetLength(UpdateManifestConstants.MaximumPackageBytes + 1);
        }
        var hashStarts = 0;

        var loaded = new VerifiedPackageStore(
            UpdatePackageVariant.WithDotnet9,
            temp.Path,
            hashStartedForTests: () => hashStarts++)
            .Load(authority.Version, authority.Variant, authority.FileName);

        Assert.Null(loaded.Record);
        Assert.True(loaded.CorruptionDetected);
        Assert.Equal(0, hashStarts);
    }

    [Fact]
    public async Task UpdateCheckHoldingAdmissionRejectsDownloadBeforeOperationNetworkOrFilesystem()
    {
        using var temp = new TempDirectory();
        var authority = CreateAuthority([1, 2, 3, 4]);
        var admission = new UpdateCheckAdmission(new IsolatedUpdateCheckExclusion());
        using var checkLease = admission.TryAcquire().Lease;
        Assert.NotNull(checkLease);
        var transport = new FakeUpdateHttpTransport(_ => throw new InvalidOperationException("must not request"));
        var operationIds = 0;
        var downloader = CreateDirectDownloader(
            temp.Path,
            authority,
            transport,
            admission,
            operationIdFactoryForTests: () => "download-test-" + Interlocked.Increment(ref operationIds));

        var result = await downloader.DownloadAsync(CancellationToken.None);

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal("already_running", result.ResultCode);
        Assert.Equal(0, operationIds);
        Assert.Empty(transport.Requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task DownloadHoldingAdmissionRejectsCheckAndSecondDownloadWithoutQueueingOrMutation()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var authority = CreateAuthority(bytes);
        var admission = new UpdateCheckAdmission(new IsolatedUpdateCheckExclusion());
        var requestEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTransport = new FakeUpdateHttpTransport(async (_, cancellationToken) =>
        {
            requestEntered.TrySetResult();
            await releaseRequest.Task.WaitAsync(cancellationToken);
            return FakeUpdateHttpTransport.Response(HttpStatusCode.OK, bytes);
        });
        var firstOperationIds = 0;
        var first = CreateDirectDownloader(
            temp.Path, authority, firstTransport, admission,
            operationIdFactoryForTests: () => "download-first-" + Interlocked.Increment(ref firstOperationIds));
        var firstRun = first.DownloadAsync(CancellationToken.None);
        await requestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = SnapshotRelativeEntries(temp.Path);

        var checkContender = admission.TryAcquire();
        var secondTransport = new FakeUpdateHttpTransport(_ => throw new InvalidOperationException("must not request"));
        var secondOperationIds = 0;
        var second = CreateDirectDownloader(
            temp.Path, authority, secondTransport, admission,
            operationIdFactoryForTests: () => "download-second-" + Interlocked.Increment(ref secondOperationIds));
        var secondResult = await second.DownloadAsync(CancellationToken.None);
        var after = SnapshotRelativeEntries(temp.Path);

        Assert.Equal(UpdateCheckAdmissionStatus.AlreadyRunning, checkContender.Status);
        Assert.Null(checkContender.Lease);
        Assert.False(secondResult.CompletedSuccessfully);
        Assert.Equal("already_running", secondResult.ResultCode);
        Assert.Equal(1, firstOperationIds);
        Assert.Equal(0, secondOperationIds);
        Assert.Empty(secondTransport.Requests);
        Assert.Equal(before, after);

        releaseRequest.TrySetResult();
        var firstResult = await firstRun;
        Assert.True(firstResult.CompletedSuccessfully, firstResult.ResultCode);
        Assert.Single(firstTransport.Requests);
    }

    private static string[] SnapshotRelativeEntries(string root)
        => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static VerifiedPackageDownloader CreateDirectDownloader(
        string updateDirectory,
        TestAuthority authority,
        IUpdateHttpTransport transport,
        UpdateCheckAdmission? admission = null,
        VerifiedPackageStore? store = null,
        Func<string>? operationIdFactoryForTests = null)
        => CreateDirectDownloader(updateDirectory, authority.Capability, authority.View, transport, admission, store, operationIdFactoryForTests);

    private static VerifiedPackageDownloader CreateDirectDownloader(
        string updateDirectory,
        object capability,
        VerifiedPackageAuthorityView view,
        IUpdateHttpTransport transport,
        UpdateCheckAdmission? admission = null,
        VerifiedPackageStore? store = null,
        Func<string>? operationIdFactoryForTests = null)
    {
        var discovery = new FakeUpdateDiscoveryClient(new UpdateDiscoveryCheckResult
        {
            Status = UpdateDiscoveryStatus.UpdateAvailable,
            ETag = null,
            Version = view.Version,
            Tag = view.Tag,
            ReleaseUrl = view.ReleaseUrl,
            VerifiedManifest = null,
            VerifiedPackageAuthority = capability,
            ErrorCategory = null,
            ErrorCode = null
        });
        return new VerifiedPackageDownloader(
            discovery,
            transport,
            store ?? new VerifiedPackageStore(UpdatePackageVariant.WithDotnet9, updateDirectory),
            new VerifiedPackageDownloadOptions
            {
                HardMaximumPackageBytes = 64 * 1024,
                RequestTimeout = TimeSpan.FromSeconds(2),
                IdleTimeout = TimeSpan.FromSeconds(2)
            },
            new ManualUpdateScheduleClock(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero)),
            admission ?? new UpdateCheckAdmission(new IsolatedUpdateCheckExclusion()),
            operationIdFactoryForTests);
    }

    private static TestAuthority CreateAuthority(byte[] bytes, UpdatePackageVariant variant = UpdatePackageVariant.WithDotnet9)
    {
        var manifest = UpdateContractTestData.CreateManifest();
        var selected = manifest["assets"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["variant"]!.GetValue<string>() == (variant == UpdatePackageVariant.WithDotnet9 ? "with-dotnet9" : "no-dotnet9"));
        selected["sizeBytes"] = bytes.LongLength;
        selected["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var signed = UpdateContractTestData.Sign(manifest);
        var scenario = new DiscoveryScenario(signed);
        var transport = new FakeUpdateHttpTransport(
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, scenario.ReleaseMetadata),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signed.ManifestBytes),
            _ => FakeUpdateHttpTransport.Response(HttpStatusCode.OK, signed.SignatureEnvelopeBytes));
        using var client = TestOnlyDiscoveryHarness.Create(
            transport,
            signed.TrustStore,
            new UpdateDiscoveryOptions { InstalledVersion = SemanticVersion.Parse("1.3.1"), InstalledVariant = variant });
        var capability = client.CheckAsync(null, CancellationToken.None).GetAwaiter().GetResult().VerifiedPackageAuthority
            ?? throw new Xunit.Sdk.XunitException("The test verifier did not create a package authority.");
        if (!GitHubUpdateDiscoveryClient.TryGetVerifiedPackageAuthority(capability, out var view))
        {
            throw new Xunit.Sdk.XunitException("The test verifier returned a non-authoritative package capability.");
        }
        return new TestAuthority(capability, view);
    }

    private sealed record TestAuthority(object Capability, VerifiedPackageAuthorityView View)
    {
        public string Tag => View.Tag;
        public string ReleaseUrl => View.ReleaseUrl;
        public string Version => View.Version;
        public UpdatePackageVariant Variant => View.Variant;
        public string FileName => View.FileName;
        public long ExpectedSize => View.ExpectedSize;
        public string ExpectedSha256 => View.ExpectedSha256;
    }

    private sealed class BusyExclusion : IUpdateCheckExclusion
    {
        public IUpdateCheckLease? TryAcquire() => null;
    }

    private sealed class ThrowAtFaultPoint(UpdateDownloadFaultPoint faultPoint) : IUpdateDownloadFaultInjector
    {
        public void ThrowIfRequested(UpdateDownloadFaultPoint point)
        {
            if (point == faultPoint)
            {
                throw new IOException($"Injected interruption at {point}.");
            }
        }
    }
}
