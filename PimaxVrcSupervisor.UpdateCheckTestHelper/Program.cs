using System.Security.Principal;
using PimaxVrcSupervisor.Updates;

return args.Length switch
{
    3 when string.Equals(args[0], "hold", StringComparison.Ordinal) => Hold(args[1], args[2]),
    1 when string.Equals(args[0], "try", StringComparison.Ordinal) => TryAcquire(),
    2 when string.Equals(args[0], "abandon", StringComparison.Ordinal) => Abandon(args[1]),
    4 when string.Equals(args[0], "hold-name", StringComparison.Ordinal) => HoldNamed(args[1], args[2], args[3]),
    2 when string.Equals(args[0], "try-name", StringComparison.Ordinal) => TryAcquireNamed(args[1]),
    3 when string.Equals(args[0], "abandon-name", StringComparison.Ordinal) => AbandonNamed(args[1], args[2]),
    _ => 64
};

static int Hold(string readyPath, string releasePath)
    => HoldExclusion(UserScopedUpdateCheckExclusion.ForCurrentUser(), readyPath, releasePath);

static int HoldNamed(string mutexName, string readyPath, string releasePath)
    => HoldExclusion(ForNamedMutex(mutexName), readyPath, releasePath);

static int HoldExclusion(UserScopedUpdateCheckExclusion exclusion, string readyPath, string releasePath)
{
    try
    {
        using var lease = exclusion.TryAcquire();
        if (lease is null)
        {
            return 3;
        }

        File.WriteAllText(readyPath, "acquired");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!File.Exists(releasePath))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return 4;
            }

            Thread.Sleep(20);
        }

        return 0;
    }
    catch
    {
        return 5;
    }
}

static int TryAcquire()
    => TryAcquireExclusion(UserScopedUpdateCheckExclusion.ForCurrentUser());

static int TryAcquireNamed(string mutexName)
    => TryAcquireExclusion(ForNamedMutex(mutexName));

static int TryAcquireExclusion(UserScopedUpdateCheckExclusion exclusion)
{
    try
    {
        var admission = new UpdateCheckAdmission(exclusion).TryAcquire();
        using var lease = admission.Lease;
        switch (admission.Status)
        {
            case UpdateCheckAdmissionStatus.Admitted:
                Console.WriteLine("acquired");
                return 0;
            case UpdateCheckAdmissionStatus.AlreadyRunning:
                Console.WriteLine("already_running");
                return 3;
            default:
                Console.WriteLine("gate_unavailable");
                return 5;
        }
    }
    catch
    {
        return 5;
    }
}

static int Abandon(string readyPath)
    => AbandonExclusion(UserScopedUpdateCheckExclusion.ForCurrentUser(), readyPath);

static int AbandonNamed(string mutexName, string readyPath)
    => AbandonExclusion(ForNamedMutex(mutexName), readyPath);

static int AbandonExclusion(UserScopedUpdateCheckExclusion exclusion, string readyPath)
{
    try
    {
        var lease = exclusion.TryAcquire();
        if (lease is null)
        {
            return 3;
        }

        File.WriteAllText(readyPath, "acquired");
        GC.KeepAlive(lease);
        Environment.Exit(0);
        return 0;
    }
    catch
    {
        return 5;
    }
}

static UserScopedUpdateCheckExclusion ForNamedMutex(string mutexName)
{
    var sid = WindowsIdentity.GetCurrent().User
        ?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
    return UserScopedUpdateCheckExclusion.ForMutexName(mutexName, sid);
}
