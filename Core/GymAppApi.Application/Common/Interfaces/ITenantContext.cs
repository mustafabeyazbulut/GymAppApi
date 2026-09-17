namespace GymAppApi.Application.Common.Interfaces;

// Populated once per request by TenantContextMiddleware (Presentation
// layer), from the caller's own Assignment rows via
// ITenantResolutionService. See GymAppApi.Infrastructure.Tenancy.
// AmbientTenantContext for the concrete, settable implementation.
public interface ITenantContext
{
    int? CompanyId { get; }
    int? BranchId { get; }
    bool IsSuperAdmin { get; }
}
