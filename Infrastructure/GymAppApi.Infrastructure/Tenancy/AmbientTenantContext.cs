using GymAppApi.Application.Common.Interfaces;

namespace GymAppApi.Infrastructure.Tenancy;

// Placeholder until the Auth plan adds JWT + a middleware that reads
// CompanyId/BranchId/Role claims into a request-scoped implementation of
// this interface. Reporting IsSuperAdmin = true means every global query
// filter in Task 7 is a no-op for now — safe default for an
// unauthenticated foundation, NOT safe once real users exist.
public class AmbientTenantContext : ITenantContext
{
    public int? CompanyId => null;
    public int? BranchId => null;
    public bool IsSuperAdmin => true;
}
