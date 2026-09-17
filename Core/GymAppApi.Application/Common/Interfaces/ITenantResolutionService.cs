namespace GymAppApi.Application.Common.Interfaces;

// Resolves what a request's ITenantContext should be, from the caller's own
// Assignment rows. Deliberately its own interface (not folded into
// ITenantContext itself) because the implementation needs to bypass the
// global query filter for one lookup (see this plan's "Key facts" section) -
// keeping that concern out of ITenantContext keeps that interface a plain,
// dependency-free settable bag of 3 properties.
public interface ITenantResolutionService
{
    Task<ResolvedTenant> ResolveForUserAsync(int userId, CancellationToken cancellationToken = default);
}

public record ResolvedTenant(bool IsSuperAdmin, int? CompanyId, int? BranchId);
