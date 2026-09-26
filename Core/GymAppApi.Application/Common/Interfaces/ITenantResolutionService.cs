using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Common.Interfaces;

// Resolves what a request's ITenantContext should be, from the caller's own
// Assignment rows. Deliberately its own interface (not folded into
// ITenantContext itself) because the implementation needs to bypass the
// global query filter for one lookup (see this plan's "Key facts" section) -
// keeping that concern out of ITenantContext keeps that interface a plain,
// dependency-free settable bag of properties.
public interface ITenantResolutionService
{
    // activeAssignmentId (X-Active-Assignment-Id header'ı): çağıranın KENDİ,
    // aktif personel atamalarından (GymAdmin/BranchManager/Trainer) birinin
    // Id'si. Verildiyse bağlam tamamen o atamadan kurulur; çağırana ait
    // değilse, pasifse veya personel ataması değilse sonuç
    // ActiveAssignmentRejected = true olur (middleware 403 döner).
    //
    // preferredCompanyId (eski X-Active-Company-Id header'ı): sadece
    // activeAssignmentId verilmediğinde, birden fazla personel ataması olan
    // çağıran için geriye dönük uyumlu bir ipucu. Asla tek başına
    // güvenilmez - sadece çağıranın KENDİ atamaları arasından seçimi
    // değiştirir, sahip olmadığı bir erişimi asla vermez.
    Task<ResolvedTenant> ResolveForUserAsync(
        int userId,
        int? preferredCompanyId = null,
        int? activeAssignmentId = null,
        CancellationToken cancellationToken = default);
}

public record ResolvedTenant(
    bool IsSuperAdmin,
    int? CompanyId,
    int? BranchId,
    int? AssignmentId = null,
    AssignmentRole? Role = null,
    bool ActiveAssignmentRejected = false,
    // Aktif atamanın firması pasif (kapatılmış) - personel yazma uçları 403
    // CompanyInactive döner (bkz. WebApi ActiveCompanyRequiredFilter).
    bool CompanyInactive = false);
