using PimaxVrcSupervisor.Updates;

internal sealed class IsolatedUpdateCheckExclusion : IUpdateCheckExclusion
{
    public IUpdateCheckLease? TryAcquire() => new Lease();

    private sealed class Lease : IUpdateCheckLease
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class SharedTestUpdateCheckExclusion : IUpdateCheckExclusion
{
    private int _active;

    public IUpdateCheckLease? TryAcquire()
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return null;
        }

        return new Lease(this);
    }

    private sealed class Lease(SharedTestUpdateCheckExclusion owner) : IUpdateCheckLease
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Exchange(ref owner._active, 0);
            }
        }
    }
}
