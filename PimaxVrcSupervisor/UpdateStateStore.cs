using System.Text.Json;
using System.Text.Json.Serialization;

namespace PimaxVrcSupervisor.Updates;

internal enum UpdateCheckPolicy
{
    Disabled,
    NotifyStable
}

internal enum UpdateChannel
{
    Stable
}

internal enum UpdateErrorCategory
{
    Offline,
    Timeout,
    Http,
    Schema,
    Signature,
    Rollback,
    ReleaseMismatch,
    State,
    Cancelled
}

internal sealed record UpdateBoundedErrorV1
{
    [JsonPropertyName("category"), JsonRequired]
    public required UpdateErrorCategory Category { get; init; }

    [JsonPropertyName("code"), JsonRequired]
    public required string Code { get; init; }

    [JsonPropertyName("atUtc"), JsonRequired]
    public required DateTimeOffset AtUtc { get; init; }
}

internal sealed record UpdateStateV1
{
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("policy"), JsonRequired]
    public required UpdateCheckPolicy Policy { get; init; }

    [JsonPropertyName("channel"), JsonRequired]
    public required UpdateChannel Channel { get; init; }

    [JsonPropertyName("installedVariant"), JsonRequired]
    public required UpdatePackageVariant InstalledVariant { get; init; }

    [JsonPropertyName("lastAttemptUtc"), JsonRequired]
    public required DateTimeOffset? LastAttemptUtc { get; init; }

    [JsonPropertyName("lastSuccessfulCheckUtc"), JsonRequired]
    public required DateTimeOffset? LastSuccessfulCheckUtc { get; init; }

    [JsonPropertyName("etag"), JsonRequired]
    public required string? ETag { get; init; }

    [JsonPropertyName("lastManifestSha256"), JsonRequired]
    public required string? LastManifestSha256 { get; init; }

    [JsonPropertyName("highestAcceptedReleaseSequence"), JsonRequired]
    public required long HighestAcceptedReleaseSequence { get; init; }

    [JsonPropertyName("highestAcceptedVersion"), JsonRequired]
    public required string? HighestAcceptedVersion { get; init; }

    [JsonPropertyName("latestVerifiedVersion"), JsonRequired]
    public required string? LatestVerifiedVersion { get; init; }

    [JsonPropertyName("latestVerifiedTag"), JsonRequired]
    public required string? LatestVerifiedTag { get; init; }

    [JsonPropertyName("latestVerifiedReleaseUrl"), JsonRequired]
    public required string? LatestVerifiedReleaseUrl { get; init; }

    [JsonPropertyName("dismissedVersion"), JsonRequired]
    public required string? DismissedVersion { get; init; }

    [JsonPropertyName("dismissedAtUtc"), JsonRequired]
    public required DateTimeOffset? DismissedAtUtc { get; init; }

    [JsonPropertyName("lastError"), JsonRequired]
    public required UpdateBoundedErrorV1? LastError { get; init; }

    // Reserved schema slots. Phase 33A rejects non-null values and implements no package path.
    [JsonPropertyName("packageDownload"), JsonRequired]
    public required object? PackageDownload { get; init; }

    [JsonPropertyName("packageStaging"), JsonRequired]
    public required object? PackageStaging { get; init; }

    [JsonPropertyName("packageInstallation"), JsonRequired]
    public required object? PackageInstallation { get; init; }

    public static UpdateStateV1 CreateDefault(UpdatePackageVariant installedVariant) => new()
    {
        SchemaVersion = 1,
        Policy = UpdateCheckPolicy.Disabled,
        Channel = UpdateChannel.Stable,
        InstalledVariant = installedVariant,
        LastAttemptUtc = null,
        LastSuccessfulCheckUtc = null,
        ETag = null,
        LastManifestSha256 = null,
        HighestAcceptedReleaseSequence = 0,
        HighestAcceptedVersion = null,
        LatestVerifiedVersion = null,
        LatestVerifiedTag = null,
        LatestVerifiedReleaseUrl = null,
        DismissedVersion = null,
        DismissedAtUtc = null,
        LastError = null,
        PackageDownload = null,
        PackageStaging = null,
        PackageInstallation = null
    };
}

internal enum UpdateStateSource
{
    Default,
    Current,
    Previous
}

internal sealed record UpdateStateLoadResult(
    UpdateStateV1 State,
    UpdateStateSource Source,
    bool CorruptionDetected);

internal sealed class UpdateStateStore
{
    public const int SchemaVersion = 1;
    public const int MaximumStateBytes = 64 * 1024;
    public const string StateFileName = "update-state-v1.json";
    public const string PreviousStateFileName = "update-state-v1.previous.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly UpdatePackageVariant _installedVariant;

    public UpdateStateStore(UpdatePackageVariant installedVariant, string? updateDirectory = null)
    {
        _installedVariant = installedVariant;
        UpdateDirectory = updateDirectory is null
            ? GetDefaultUpdateDirectory()
            : Path.GetFullPath(updateDirectory);
        StatePath = Path.Combine(UpdateDirectory, StateFileName);
        PreviousStatePath = Path.Combine(UpdateDirectory, PreviousStateFileName);
    }

    public string UpdateDirectory { get; }

    public string StatePath { get; }

    public string PreviousStatePath { get; }

    public static string GetDefaultUpdateDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PimaxVrcSupervisor",
            "Update");

    public UpdateStateLoadResult Load()
    {
        var current = TryLoad(StatePath);
        if (current.State is not null)
        {
            return new UpdateStateLoadResult(current.State, UpdateStateSource.Current, CorruptionDetected: false);
        }

        var previous = TryLoad(PreviousStatePath);
        if (previous.State is not null)
        {
            return new UpdateStateLoadResult(
                previous.State,
                UpdateStateSource.Previous,
                CorruptionDetected: current.Corrupt);
        }

        return new UpdateStateLoadResult(
            UpdateStateV1.CreateDefault(_installedVariant),
            UpdateStateSource.Default,
            CorruptionDetected: current.Corrupt || previous.Corrupt);
    }

    public async Task SaveAsync(UpdateStateV1 state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        if (state.InstalledVariant != _installedVariant)
        {
            throw new UpdateContractException("installed_variant", "The persisted package variant must match the currently installed variant.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(UpdateDirectory);
            temporaryPath = Path.Combine(UpdateDirectory, $".{StateFileName}.{Guid.NewGuid():N}.tmp");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            if (bytes.Length > MaximumStateBytes)
            {
                throw new UpdateContractException("state_size", "The update state exceeds its size bound.");
            }

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(StatePath))
            {
                File.Replace(temporaryPath, StatePath, PreviousStatePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, StatePath);
            }

            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // The final state was not replaced; a uniquely named temporary file is harmless.
                }
            }

            _writeLock.Release();
        }
    }

    internal static void ValidateState(UpdateStateV1 state)
    {
        if (state.SchemaVersion != SchemaVersion)
        {
            throw new UpdateContractException("state_schema", "The update state schema version is not supported.");
        }

        if (!Enum.IsDefined(state.Policy)
            || state.Channel != UpdateChannel.Stable
            || !Enum.IsDefined(state.InstalledVariant))
        {
            throw new UpdateContractException("state_enum", "The update state contains an unsupported policy, channel, or package variant.");
        }

        ValidateOptionalVersion(state.HighestAcceptedVersion, "highest_version");
        ValidateOptionalVersion(state.LatestVerifiedVersion, "latest_version");
        ValidateOptionalVersion(state.DismissedVersion, "dismissed_version");
        if (state.HighestAcceptedReleaseSequence < 0)
        {
            throw new UpdateContractException("state_sequence", "The highest accepted release sequence cannot be negative.");
        }

        if (state.LastManifestSha256 is not null)
        {
            UpdateContractValidation.ValidateSha256(state.LastManifestSha256, "state_manifest_hash");
        }

        if (state.ETag is { Length: > 1024 } || state.ETag?.Any(char.IsControl) == true)
        {
            throw new UpdateContractException("state_etag", "The cached ETag is outside its safe bound.");
        }

        if (state.LatestVerifiedVersion is null)
        {
            if (state.LatestVerifiedTag is not null || state.LatestVerifiedReleaseUrl is not null)
            {
                throw new UpdateContractException("state_release", "Release tag and URL require a latest verified version.");
            }
        }
        else
        {
            var version = SemanticVersion.Parse(state.LatestVerifiedVersion);
            if (!string.Equals(state.LatestVerifiedTag, "v" + version, StringComparison.Ordinal)
                || !string.Equals(
                    state.LatestVerifiedReleaseUrl,
                    $"https://github.com/{UpdateManifestConstants.Repository}/releases/tag/v{version}",
                    StringComparison.Ordinal))
            {
                throw new UpdateContractException("state_release", "The cached release identity is inconsistent.");
            }
        }

        if (state.DismissedVersion is null != state.DismissedAtUtc is null)
        {
            throw new UpdateContractException("state_dismissal", "A dismissed version and its timestamp must be present together.");
        }

        if (state.LastError is { } error
            && (!Enum.IsDefined(error.Category)
                || !UpdateContractValidation.IsSafeIdentifier(error.Code, maximumLength: 64)))
        {
            throw new UpdateContractException("state_error", "The persisted update error is outside its bounded schema.");
        }

        if (state.PackageDownload is not null
            || state.PackageStaging is not null
            || state.PackageInstallation is not null)
        {
            throw new UpdateContractException("state_package_reserved", "Phase 33A package fields are reserved and must remain null.");
        }
    }

    private (UpdateStateV1? State, bool Corrupt) TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return (null, false);
        }

        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length is <= 0 or > MaximumStateBytes)
            {
                return (null, true);
            }

            var bytes = File.ReadAllBytes(path);
            StrictUpdateJson.ValidateSyntax(bytes, "state_json");
            var state = JsonSerializer.Deserialize<UpdateStateV1>(bytes, JsonOptions)
                ?? throw new JsonException("The update state is null.");
            ValidateState(state);
            if (state.InstalledVariant != _installedVariant)
            {
                state = state with { InstalledVariant = _installedVariant };
            }

            return (state, false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or UpdateContractException or IOException or UnauthorizedAccessException)
        {
            return (null, true);
        }
    }

    private static void ValidateOptionalVersion(string? value, string code)
    {
        if (value is null)
        {
            return;
        }

        if (!SemanticVersion.TryParse(value, out var version)
            || !version.IsStable
            || version.BuildMetadata is not null
            || !string.Equals(value, version.ToString(), StringComparison.Ordinal))
        {
            throw new UpdateContractException(code, "The persisted version is not a normalized stable semantic version.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            WriteIndented = true
        };
        options.Converters.Add(new UpdatePolicyJsonConverter());
        options.Converters.Add(new UpdateChannelJsonConverter());
        options.Converters.Add(new UpdatePackageVariantJsonConverter());
        options.Converters.Add(new UpdateErrorCategoryJsonConverter());
        options.Converters.Add(new StrictUtcDateTimeOffsetJsonConverter());
        return options;
    }
}

internal sealed class UpdatePolicyJsonConverter : ExactStringEnumJsonConverter<UpdateCheckPolicy>
{
    public UpdatePolicyJsonConverter()
        : base(new Dictionary<string, UpdateCheckPolicy>(StringComparer.Ordinal)
        {
            ["disabled"] = UpdateCheckPolicy.Disabled,
            ["notifyStable"] = UpdateCheckPolicy.NotifyStable
        })
    {
    }
}

internal sealed class UpdateChannelJsonConverter : ExactStringEnumJsonConverter<UpdateChannel>
{
    public UpdateChannelJsonConverter()
        : base(new Dictionary<string, UpdateChannel>(StringComparer.Ordinal)
        {
            ["stable"] = UpdateChannel.Stable
        })
    {
    }
}

internal sealed class UpdatePackageVariantJsonConverter : ExactStringEnumJsonConverter<UpdatePackageVariant>
{
    public UpdatePackageVariantJsonConverter()
        : base(new Dictionary<string, UpdatePackageVariant>(StringComparer.Ordinal)
        {
            ["with-dotnet9"] = UpdatePackageVariant.WithDotnet9,
            ["no-dotnet9"] = UpdatePackageVariant.NoDotnet9
        })
    {
    }
}

internal sealed class UpdateErrorCategoryJsonConverter : ExactStringEnumJsonConverter<UpdateErrorCategory>
{
    public UpdateErrorCategoryJsonConverter()
        : base(Enum.GetValues<UpdateErrorCategory>().ToDictionary(
            value => char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..],
            value => value,
            StringComparer.Ordinal))
    {
    }
}

internal abstract class ExactStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly IReadOnlyDictionary<string, TEnum> _fromWire;
    private readonly IReadOnlyDictionary<TEnum, string> _toWire;

    protected ExactStringEnumJsonConverter(IReadOnlyDictionary<string, TEnum> values)
    {
        _fromWire = values;
        _toWire = values.ToDictionary(pair => pair.Value, pair => pair.Key);
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || reader.GetString() is not { } value
            || !_fromWire.TryGetValue(value, out var result))
        {
            throw new JsonException($"Unsupported {typeof(TEnum).Name} value.");
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!_toWire.TryGetValue(value, out var wireValue))
        {
            throw new JsonException($"Unsupported {typeof(TEnum).Name} value.");
        }

        writer.WriteStringValue(wireValue);
    }
}

internal sealed class StrictUtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || reader.GetString() is not { } value
            || !value.EndsWith('Z')
            || !DateTimeOffset.TryParseExact(
                value,
                Format,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var result))
        {
            throw new JsonException("The timestamp must be a strict UTC value ending in Z.");
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.UtcDateTime.ToString(Format, System.Globalization.CultureInfo.InvariantCulture));
}
