namespace GymAppApi.Application.Common.Interfaces;

// Populated from JWT claims by WebApi middleware in a later plan (Auth).
// Until then, GymAppApi.Infrastructure ships an AmbientTenantContext stub
// that always reports IsSuperAdmin = true (no filtering) so the Branch
// vertical slice in this plan is testable without auth.
public interface ITenantContext
{
    int? CompanyId { get; }
    int? BranchId { get; }
    bool IsSuperAdmin { get; }
}
