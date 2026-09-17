using GymAppApi.Application.Common.Interfaces;

namespace GymAppApi.Infrastructure.Tenancy;

// Populated once per request by TenantContextMiddleware (Presentation layer)
// right after authentication, from the caller's own Assignment rows (via
// ITenantResolutionService). A brand-new request that hasn't gone through
// that middleware yet (or an unauthenticated one) keeps these fail-closed
// defaults - IsSuperAdmin=false, CompanyId=null - which the existing global
// query filters already treat as "see nothing" for every tenant-scoped
// entity except a null-CompanyId row.
public class AmbientTenantContext : ITenantContext
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
    public bool IsSuperAdmin { get; set; }
}
