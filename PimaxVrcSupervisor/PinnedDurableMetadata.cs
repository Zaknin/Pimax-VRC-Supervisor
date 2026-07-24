using Microsoft.Win32.SafeHandles;

namespace PimaxVrcSupervisor.Updates;

// Phase 33B keeps current and previous metadata in the same pinned directory.
// Each pathname operation is performed through the containing directory handle;
// recovery reads the pinned object it validated rather than reopening a pathname.
internal static class PinnedDurableMetadata
{
    internal sealed record LoadResult<T>(T? Value, bool UsedPrevious, bool CurrentInvalid, bool PreviousInvalid)
        where T : class;

    internal static async Task WriteAsync(
        HardenedPackageDirectory directory,
        string currentName,
        string previousName,
        ReadOnlyMemory<byte> bytes,
        int maximumBytes,
        CancellationToken cancellationToken,
        Action? afterTemporaryWrite = null,
        Action? afterPreviousPreparation = null,
        Action? afterCurrentReplacement = null,
        Action? afterCurrentDurability = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        HardenedUpdateStagingStore.ValidateComponent(currentName, "package_metadata_name");
        HardenedUpdateStagingStore.ValidateComponent(previousName, "package_metadata_name");
        if (string.Equals(currentName, previousName, StringComparison.Ordinal)
            || bytes.Length <= 0
            || bytes.Length > maximumBytes)
        {
            throw new UpdateContractException("package_metadata_size", "The durable metadata payload is outside its exact bounded contract.");
        }

        var temporaryName = "." + currentName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var temporary = directory.CreateNewFile(temporaryName, write: true);
        var temporaryIdentity = directory.CaptureFileIdentity(temporary);
        var promoted = false;
        try
        {
            await RandomAccess.WriteAsync(temporary, bytes, 0, cancellationToken).ConfigureAwait(false);
            afterTemporaryWrite?.Invoke();
            directory.FlushPinnedFile(temporary);

            using (var current = directory.TryOpenExistingFileForRecovery(currentName))
            {
                if (current is not null)
                {
                    var currentIdentity = directory.CaptureFileIdentity(current);
                    directory.VerifyFinalFilePath(current, currentName, "package_metadata_current");
                    if (directory.CaptureFileIdentity(current) != currentIdentity)
                    {
                        throw new UpdateContractException("package_metadata_identity", "The current metadata identity changed before previous-copy preparation.");
                    }

                    directory.ReplacePinnedFile(current, previousName);
                    directory.FlushPinnedDirectory();
                }
            }

            afterPreviousPreparation?.Invoke();
            directory.RenamePinnedFile(temporary, currentName);
            promoted = true;
            afterCurrentReplacement?.Invoke();
            directory.FlushPinnedFile(temporary);
            directory.FlushPinnedDirectory();
            afterCurrentDurability?.Invoke();
        }
        catch (Exception primary)
        {
            if (promoted)
            {
                throw;
            }

            try
            {
                directory.DeletePinnedFile(temporary, temporaryIdentity, temporaryName);
                directory.FlushPinnedDirectory();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Durable metadata persistence and exact temporary cleanup both failed.", primary, cleanup);
            }

            throw;
        }
    }

    // Repairs a corrupt current copy from an already validated previous copy without
    // rotating that previous copy away. A crash during this repair therefore retains
    // the prior durable journal as the recovery anchor.
    internal static async Task RepairCurrentAsync(
        HardenedPackageDirectory directory,
        string currentName,
        ReadOnlyMemory<byte> bytes,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        HardenedUpdateStagingStore.ValidateComponent(currentName, "package_metadata_name");
        if (bytes.Length <= 0 || bytes.Length > maximumBytes)
        {
            throw new UpdateContractException("package_metadata_size", "The durable metadata payload is outside its exact bounded contract.");
        }

        var temporaryName = "." + currentName + "." + Guid.NewGuid().ToString("N") + ".repair.tmp";
        using var temporary = directory.CreateNewFile(temporaryName, write: true);
        var temporaryIdentity = directory.CaptureFileIdentity(temporary);
        var promoted = false;
        try
        {
            await RandomAccess.WriteAsync(temporary, bytes, 0, cancellationToken).ConfigureAwait(false);
            directory.FlushPinnedFile(temporary);
            directory.ReplacePinnedFile(temporary, currentName);
            promoted = true;
            directory.FlushPinnedFile(temporary);
            directory.FlushPinnedDirectory();
        }
        catch (Exception primary)
        {
            if (promoted)
            {
                throw;
            }

            try
            {
                directory.DeletePinnedFile(temporary, temporaryIdentity, temporaryName);
                directory.FlushPinnedDirectory();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Durable metadata repair and exact temporary cleanup both failed.", primary, cleanup);
            }

            throw;
        }
    }

    internal static LoadResult<T> Load<T>(
        HardenedPackageDirectory directory,
        string currentName,
        string previousName,
        int maximumBytes,
        Func<ReadOnlyMemory<byte>, T> deserializeAndValidate,
        Func<T, bool>? previousIsAdmissible = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(deserializeAndValidate);
        var current = TryLoad(directory, currentName, maximumBytes, deserializeAndValidate);
        if (current.Value is not null)
        {
            return new LoadResult<T>(current.Value, UsedPrevious: false, CurrentInvalid: false, PreviousInvalid: false);
        }

        var previous = TryLoad(directory, previousName, maximumBytes, deserializeAndValidate);
        if (previous.Value is not null && (previousIsAdmissible?.Invoke(previous.Value) ?? true))
        {
            return new LoadResult<T>(previous.Value, UsedPrevious: true, CurrentInvalid: current.Invalid, PreviousInvalid: false);
        }

        return new LoadResult<T>(null, UsedPrevious: false, CurrentInvalid: current.Invalid, PreviousInvalid: previous.Invalid || previous.Value is not null);
    }

    private static (T? Value, bool Invalid) TryLoad<T>(
        HardenedPackageDirectory directory,
        string name,
        int maximumBytes,
        Func<ReadOnlyMemory<byte>, T> deserializeAndValidate)
        where T : class
    {
        try
        {
            using var handle = directory.TryOpenExistingFileForRecovery(name);
            if (handle is null)
            {
                return (null, false);
            }

            var length = directory.GetPinnedFileLength(handle);
            if (length <= 0 || length > maximumBytes)
            {
                return (null, true);
            }

            var bytes = new byte[checked((int)length)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
                if (read == 0)
                {
                    return (null, true);
                }

                offset += read;
            }

            return (deserializeAndValidate(bytes), false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or UpdateContractException or NotSupportedException)
        {
            return (null, true);
        }
    }
}
