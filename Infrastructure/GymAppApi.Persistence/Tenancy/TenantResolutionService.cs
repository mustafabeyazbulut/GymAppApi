using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Tenancy;

public class TenantResolutionService : ITenantResolutionService
{
    private readonly GymAppApiDbContext _dbContext;

    public TenantResolutionService(GymAppApiDbContext dbContext) => _dbContext = dbContext;

    public async Task<ResolvedTenant> ResolveForUserAsync(int userId, int? preferredCompanyId = null, CancellationToken cancellationToken = default)
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

        // Birden fazla SuperAdmin-olmayan Assignment'ı olan bir çağıran
        // (çok-şirketli personel/üye), istek verildiğinde ve gerçekten
        // eşleştiğinde isteğin kendi X-Active-Company-Id header'ıyla (bkz.
        // TenantContextMiddleware) eşleşen Assignment'a çözülür - bu sadece
        // bir İPUCU, çağıranın KENDİ mevcut satırları arasından seçim
        // yapmanın ötesinde asla güvenilmez, bu yüzden Assignment'ı olmadığı
        // bir şirkete erişim veremez. Header gönderilmediğinde veya hiçbir
        // şeyle eşleşmediğinde çağıranın kronolojik olarak ilk Assignment'ına
        // (önceki davranış) geri döner, bu yüzden tek-şirketli bir çağıran
        // (yaygın durum) ve header'ı henüz göndermeyen herhangi bir istemci
        // tamamen etkilenmez.
        var preferred = preferredCompanyId is null
            ? null
            : assignments.Where(a => a.CompanyId == preferredCompanyId).MinBy(a => a.Id);
        var primary = preferred ?? assignments.OrderBy(a => a.Id).First();
        return new ResolvedTenant(false, primary.CompanyId, primary.BranchId);
    }
}
