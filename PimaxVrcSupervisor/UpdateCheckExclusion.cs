using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PimaxVrcSupervisor.Updates;

internal interface IUpdateCheckLease : IDisposable
{
}

internal interface IUpdateCheckExclusion
{
    IUpdateCheckLease? TryAcquire();
}

internal enum UpdateCheckAdmissionStatus
{
    Admitted,
    AlreadyRunning,
    Unavailable
}

internal sealed record UpdateCheckAdmissionResult(
    UpdateCheckAdmissionStatus Status,
    UpdateCheckAdmissionLease? Lease,
    string? ErrorCode)
{
    public bool IsAdmitted => Status == UpdateCheckAdmissionStatus.Admitted && Lease is not null;
}

internal sealed class UpdateCheckAdmission
{
    private readonly IUpdateCheckExclusion _crossProcessExclusion;
    private int _processActive;

    public UpdateCheckAdmission(IUpdateCheckExclusion crossProcessExclusion)
    {
        _crossProcessExclusion = crossProcessExclusion ?? throw new ArgumentNullException(nameof(crossProcessExclusion));
    }

    public bool IsActive => Volatile.Read(ref _processActive) == 1;

    public UpdateCheckAdmissionResult TryAcquire()
    {
        if (Interlocked.CompareExchange(ref _processActive, 1, 0) != 0)
        {
            return new UpdateCheckAdmissionResult(
                UpdateCheckAdmissionStatus.AlreadyRunning,
                Lease: null,
                ErrorCode: "already_running");
        }

        try
        {
            var crossProcessLease = _crossProcessExclusion.TryAcquire();
            if (crossProcessLease is null)
            {
                Interlocked.Exchange(ref _processActive, 0);
                return new UpdateCheckAdmissionResult(
                    UpdateCheckAdmissionStatus.AlreadyRunning,
                    Lease: null,
                    ErrorCode: "already_running");
            }

            return new UpdateCheckAdmissionResult(
                UpdateCheckAdmissionStatus.Admitted,
                new UpdateCheckAdmissionLease(crossProcessLease, ReleaseProcessExclusion),
                ErrorCode: null);
        }
        catch
        {
            Interlocked.Exchange(ref _processActive, 0);
            return new UpdateCheckAdmissionResult(
                UpdateCheckAdmissionStatus.Unavailable,
                Lease: null,
                ErrorCode: "gate_unavailable");
        }
    }

    private void ReleaseProcessExclusion()
        => Interlocked.Exchange(ref _processActive, 0);
}

internal sealed class UpdateCheckAdmissionLease(
    IUpdateCheckLease crossProcessLease,
    Action releaseProcessExclusion) : IDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            crossProcessLease.Dispose();
        }
        finally
        {
            releaseProcessExclusion();
        }
    }
}

internal sealed class UserScopedUpdateCheckExclusion : IUpdateCheckExclusion
{
    private const string MutexPrefix = @"Global\PimaxVrcSupervisor.UpdateCheck.";
    internal const MutexRights RequiredRights = MutexRights.Synchronize | MutexRights.Modify | MutexRights.ReadPermissions;
    private readonly string _mutexName;
    private readonly SecurityIdentifier _userSid;
    private readonly Func<Mutex, MutexSecurity> _readSecurity;
    private readonly Action? _abandonedObserver;

    private UserScopedUpdateCheckExclusion(
        string mutexName,
        SecurityIdentifier userSid,
        Func<Mutex, MutexSecurity>? readSecurity = null,
        Action? abandonedObserver = null)
    {
        _mutexName = mutexName;
        _userSid = userSid;
        _readSecurity = readSecurity ?? ThreadingAclExtensions.GetAccessControl;
        _abandonedObserver = abandonedObserver;
    }

    internal string MutexName => _mutexName;

    public static UserScopedUpdateCheckExclusion ForCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Secure update-check exclusion requires Windows.");
        }

        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
        return ForSid(sid);
    }

    internal static UserScopedUpdateCheckExclusion ForSid(SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        return new UserScopedUpdateCheckExclusion(BuildMutexName(sid), sid);
    }

    internal static UserScopedUpdateCheckExclusion ForMutexName(
        string mutexName,
        SecurityIdentifier sid,
        Func<Mutex, MutexSecurity>? readSecurity = null,
        Action? abandonedObserver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentNullException.ThrowIfNull(sid);
        if (!mutexName.StartsWith(MutexPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The update-check mutex must use the bounded Global identity prefix.", nameof(mutexName));
        }

        return new UserScopedUpdateCheckExclusion(mutexName, sid, readSecurity, abandonedObserver);
    }

    internal static string BuildMutexName(SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid.Value)));
        return MutexPrefix + suffix;
    }

    internal static MutexSecurity BuildMutexSecurity(SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        var security = new MutexSecurity();
        security.SetOwner(sid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new MutexAccessRule(sid, RequiredRights, AccessControlType.Allow));
        return security;
    }

    public IUpdateCheckLease? TryAcquire()
    {
        var ready = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        Exception? failure = null;
        var acquired = false;
        var owner = new Thread(() =>
        {
            Mutex? mutex = null;
            try
            {
                mutex = OpenOrCreateSecureMutex();
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                    _abandonedObserver?.Invoke();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                ready.Set();
            }

            if (failure is not null || !acquired || mutex is null)
            {
                mutex?.Dispose();
                return;
            }

            try
            {
                release.Wait();
                mutex.ReleaseMutex();
            }
            catch
            {
                // Process/thread exit abandons ownership safely if release itself fails.
            }
            finally
            {
                mutex.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "PimaxVrcSupervisor update-check mutex owner"
        };
        owner.Start();
        ready.Wait();

        if (failure is not null)
        {
            release.Set();
            owner.Join();
            ready.Dispose();
            release.Dispose();
            throw new IOException("The secure global update-check exclusion could not be established.", failure);
        }

        if (!acquired)
        {
            owner.Join();
            ready.Dispose();
            release.Dispose();
            return null;
        }

        return new Lease(owner, ready, release);
    }

    private Mutex OpenOrCreateSecureMutex()
    {
        Mutex? mutex = null;
        try
        {
            try
            {
                mutex = MutexAcl.Create(
                    initiallyOwned: false,
                    _mutexName,
                    out _,
                    BuildMutexSecurity(_userSid));
            }
            catch (UnauthorizedAccessException)
            {
                mutex = MutexAcl.OpenExisting(_mutexName, RequiredRights);
            }

            ValidateMutexSecurity(mutex, _userSid, _readSecurity);
            return mutex;
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    internal static void ValidateMutexSecurity(
        Mutex mutex,
        SecurityIdentifier expectedSid,
        Func<Mutex, MutexSecurity>? readSecurity = null)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        ArgumentNullException.ThrowIfNull(expectedSid);
        var security = (readSecurity ?? ThreadingAclExtensions.GetAccessControl)(mutex)
            ?? throw new UnauthorizedAccessException("The global update-check mutex security descriptor is unavailable.");
        ValidateMutexSecurityDescriptor(security, expectedSid);
    }

    internal static void ValidateMutexSecurityDescriptor(
        MutexSecurity security,
        SecurityIdentifier expectedSid,
        Func<MutexSecurity, IdentityReference?>? readOwner = null)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(expectedSid);
        IdentityReference? owner;
        try
        {
            owner = (readOwner ?? (static descriptor => descriptor.GetOwner(typeof(SecurityIdentifier))))(security);
        }
        catch (Exception exception)
        {
            throw new UnauthorizedAccessException("The global update-check mutex owner could not be read.", exception);
        }

        if (owner is not SecurityIdentifier ownerSid || !ownerSid.Equals(expectedSid))
        {
            throw new UnauthorizedAccessException("The global update-check mutex owner is not the expected current-user SID.");
        }

        if (!security.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException("The global update-check mutex ACL is not protected.");
        }

        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));
        var accessRules = rules.Cast<MutexAccessRule>().ToArray();
        if (accessRules.Length != 1)
        {
            throw new UnauthorizedAccessException("The global update-check mutex ACL is not the exact one-rule contract.");
        }

        var rule = accessRules[0];
        if (rule.IsInherited
            || rule.IdentityReference is not SecurityIdentifier ruleSid
            || !ruleSid.Equals(expectedSid)
            || rule.AccessControlType != AccessControlType.Allow)
        {
            throw new UnauthorizedAccessException("The global update-check mutex ACL contains an unexpected access rule.");
        }

        if (rule.MutexRights != RequiredRights)
        {
            throw new UnauthorizedAccessException("The global update-check mutex ACL does not exactly match the bounded rights contract.");
        }
    }

    private sealed class Lease(
        Thread owner,
        ManualResetEventSlim ready,
        ManualResetEventSlim release) : IUpdateCheckLease
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            release.Set();
            owner.Join();
            ready.Dispose();
            release.Dispose();
        }
    }
}
