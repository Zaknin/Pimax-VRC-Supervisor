namespace PimaxVrcSupervisor.Updates;

// Production update trust roots live only in this file so additions and rotations have a
// single auditable diff. Never copy test key material into this registry. Until an approved
// offline public key is supplied, the empty registry fails every production verification closed.
internal static class ProductionUpdateTrustRoots
{
    private static readonly UpdateTrustRoot[] AuditedRoots = [];

    public static UpdateTrustStore CreateTrustStore() => new(AuditedRoots);
}
