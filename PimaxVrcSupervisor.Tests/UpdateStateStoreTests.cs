using System.Text.Json;
using System.Text.Json.Nodes;
using PimaxVrcSupervisor.Updates;
using Xunit;

public sealed class UpdateStateStoreTests
{
    [Fact]
    public void DefaultLocationIsOutsideInstallationUnderLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PimaxVrcSupervisor",
            "Update");

        Assert.Equal(expected, UpdateStateStore.GetDefaultUpdateDirectory());
    }

    [Fact]
    public async Task StateRoundTripsThroughVersionedFile()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var state = UpdateContractTestData.CreateState(
            lastSuccessfulCheckUtc: new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));

        await store.SaveAsync(state, CancellationToken.None);
        var loaded = store.Load();

        Assert.Equal(UpdateStateSource.Current, loaded.Source);
        Assert.False(loaded.CorruptionDetected);
        Assert.Equal(state, loaded.State);
        Assert.True(File.Exists(Path.Combine(temp.Path, UpdateStateStore.StateFileName)));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task AtomicReplacePreservesRecoverablePreviousState()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var first = UpdateContractTestData.CreateState() with { ETag = "\"first\"" };
        var second = first with { ETag = "\"second\"" };
        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        await File.WriteAllTextAsync(store.StatePath, "{broken", CancellationToken.None);
        var loaded = store.Load();

        Assert.Equal(UpdateStateSource.Previous, loaded.Source);
        Assert.True(loaded.CorruptionDetected);
        Assert.Equal("\"first\"", loaded.State.ETag);
        Assert.True(File.Exists(store.PreviousStatePath));
    }

    [Fact]
    public async Task MalformedCurrentAndPreviousStateRecoverToDisabledDefault()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, temp.Path);
        await File.WriteAllTextAsync(store.StatePath, "[]", CancellationToken.None);
        await File.WriteAllTextAsync(store.PreviousStatePath, "{\"schemaVersion\":99}", CancellationToken.None);

        var loaded = store.Load();

        Assert.Equal(UpdateStateSource.Default, loaded.Source);
        Assert.True(loaded.CorruptionDetected);
        Assert.Equal(UpdateCheckPolicy.Disabled, loaded.State.Policy);
        Assert.Equal(UpdatePackageVariant.NoDotnet9, loaded.State.InstalledVariant);
    }

    [Fact]
    public async Task UnknownPropertyIsRecoverableCorruption()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(UpdateContractTestData.CreateState(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.StatePath))!.AsObject();
        json["unknown"] = true;
        await File.WriteAllTextAsync(store.StatePath, json.ToJsonString());

        var loaded = store.Load();

        Assert.Equal(UpdateStateSource.Default, loaded.Source);
        Assert.True(loaded.CorruptionDetected);
        Assert.Equal(UpdateCheckPolicy.Disabled, loaded.State.Policy);
    }

    [Fact]
    public async Task DuplicateStatePropertyIsRecoverableCorruption()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(UpdateContractTestData.CreateState(), CancellationToken.None);
        var json = await File.ReadAllTextAsync(store.StatePath);
        json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1,\n  \"schemaVersion\": 1", StringComparison.Ordinal);
        await File.WriteAllTextAsync(store.StatePath, json);

        var loaded = store.Load();

        Assert.True(loaded.CorruptionDetected);
        Assert.Equal(UpdateCheckPolicy.Disabled, loaded.State.Policy);
    }

    [Fact]
    public async Task MalformedTimestampIsRecoverableCorruption()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(UpdateContractTestData.CreateState(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.StatePath))!.AsObject();
        json["lastSuccessfulCheckUtc"] = "not-a-date";
        await File.WriteAllTextAsync(store.StatePath, json.ToJsonString());

        var loaded = store.Load();

        Assert.True(loaded.CorruptionDetected);
        Assert.Equal(UpdateCheckPolicy.Disabled, loaded.State.Policy);
    }

    [Fact]
    public async Task ReservedPackageFieldsMustRemainNull()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await store.SaveAsync(UpdateContractTestData.CreateState(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.StatePath))!.AsObject();
        json["packageDownload"] = new JsonObject { ["url"] = "https://example.invalid/package.zip" };
        await File.WriteAllTextAsync(store.StatePath, json.ToJsonString());

        var loaded = store.Load();

        Assert.True(loaded.CorruptionDetected);
        Assert.Null(loaded.State.PackageDownload);
        Assert.Equal(UpdateCheckPolicy.Disabled, loaded.State.Policy);
    }

    [Fact]
    public async Task PersistedVariantCannotReplaceDetectedInstalledVariant()
    {
        using var temp = new TempDirectory();
        var writer = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        await writer.SaveAsync(UpdateContractTestData.CreateState(UpdatePackageVariant.WithDotnet9), CancellationToken.None);

        var reader = new UpdateStateStore(UpdatePackageVariant.NoDotnet9, temp.Path);
        var loaded = reader.Load();

        Assert.Equal(UpdatePackageVariant.NoDotnet9, loaded.State.InstalledVariant);
    }

    [Fact]
    public async Task SaveRejectsNonNullReservedFieldsWithoutTouchingCurrentState()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var valid = UpdateContractTestData.CreateState();
        await store.SaveAsync(valid, CancellationToken.None);
        var before = await File.ReadAllBytesAsync(store.StatePath);

        var exception = await Assert.ThrowsAsync<UpdateContractException>(() => store.SaveAsync(
            valid with { PackageInstallation = new object() },
            CancellationToken.None));

        Assert.Equal("state_package_reserved", exception.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(store.StatePath));
    }

    [Fact]
    public async Task StateUsesStrictSchemaAndBoundedError()
    {
        using var temp = new TempDirectory();
        var store = new UpdateStateStore(UpdatePackageVariant.WithDotnet9, temp.Path);
        var state = UpdateContractTestData.CreateState() with
        {
            LastError = new UpdateBoundedErrorV1
            {
                Category = UpdateErrorCategory.Timeout,
                Code = "request_timeout",
                AtUtc = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero)
            }
        };

        await store.SaveAsync(state, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(store.StatePath));

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("notifyStable", document.RootElement.GetProperty("policy").GetString());
        Assert.Equal("stable", document.RootElement.GetProperty("channel").GetString());
        Assert.Equal("timeout", document.RootElement.GetProperty("lastError").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("packageDownload").ValueKind);
    }
}
