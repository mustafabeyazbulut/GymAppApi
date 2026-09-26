using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Infrastructure.Tenancy;

// Populated once per request by TenantContextMiddleware (Presentation layer)
// right after authentication, from the caller's own Assignment rows (via
// ITenantResolutionService). A brand-new request that hasn't gone through
// that middleware yet (or an unauthenticated one) keeps these fail-closed
// defaults - IsSuperAdmin=false, CompanyId=null, Role=null - which the
// existing global query filters and AssignmentRoleAuthorizationHandler
// already treat as "see nothing" / "no role".
public class AmbientTenantContext : ITenantContext
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
    public bool IsSuperAdmin { get; set; }
    public int? AssignmentId { get; set; }
    public AssignmentRole? Role { get; set; }
    public bool CompanyInactive { get; set; }
}
