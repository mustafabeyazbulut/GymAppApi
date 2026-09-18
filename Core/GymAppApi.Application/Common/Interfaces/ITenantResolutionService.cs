namespace GymAppApi.Application.Common.Interfaces;

// Resolves what a request's ITenantContext should be, from the caller's own
// Assignment rows. Deliberately its own interface (not folded into
// ITenantContext itself) because the implementation needs to bypass the
// global query filter for one lookup (see this plan's "Key facts" section) -
// keeping that concern out of ITenantContext keeps that interface a plain,
// dependency-free settable bag of 3 properties.
public interface ITenantResolutionService
{
    // preferredCompanyId: birden fazla şirkette Assignment'ı olan bir çağıran
    // için opsiyonel bir ipucu (nereden geldiği için TenantContextMiddleware'in
    // kendi yorumuna bakın) - bu olmadan, çok-şirketli bir personel her zaman
    // kronolojik olarak İLK Assignment'ının şirketine çözülür, bu da
    // gerçekten yönettiği başka herhangi bir şirketteki her işlemi sessizce
    // engeller. İpucu asla tek başına güvenilmez - sadece çağıranın KENDİ
    // mevcut Assignment'larından hangisinin seçileceğini değiştirir, zaten
    // sahip olmadığı bir erişimi asla vermez.
    Task<ResolvedTenant> ResolveForUserAsync(int userId, int? preferredCompanyId = null, CancellationToken cancellationToken = default);
}

public record ResolvedTenant(bool IsSuperAdmin, int? CompanyId, int? BranchId);
