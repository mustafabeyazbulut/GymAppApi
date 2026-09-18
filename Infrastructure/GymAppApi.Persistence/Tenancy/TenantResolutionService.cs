using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Tenancy;

public class TenantResolutionService : ITenantResolutionService
{
    private readonly GymAppApiDbContext _dbContext;

    public TenantResolutionService(GymAppApiDbContext dbContext) => _dbContext = dbContext;

    public async Task<ResolvedTenant> ResolveForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        // One of the few places allowed to bypass the tenant query filter (see
        // also GetMeQueryHandler, same rationale) — resolving a user's OWN
        // assignments must not itself already be tenant-filtered.
        var assignments = await _dbContext.Assignments
            .IgnoreQueryFilters()
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
        {
            return new ResolvedTenant(false, null, null);
        }
        if (assignments.Any(a => a.Role == AssignmentRole.SuperAdmin))
        {
            return new ResolvedTenant(true, null, null);
        }

        // A user with more than one non-SuperAdmin Assignment (multi-company
        // staff/members) picks their FIRST one deterministically for now - a
        // proper "şirket/şube seç" context switcher (see the product design
        // doc's "Çoklu Şirkete Bağlılık ve Giriş Akışı") is future work, out
        // of scope here. Practically rare today since Tenant Onboarding is
        // what starts letting a user hold more than one Assignment at all.
        var primary = assignments.OrderBy(a => a.Id).First();
        return new ResolvedTenant(false, primary.CompanyId, primary.BranchId);
    }
}
