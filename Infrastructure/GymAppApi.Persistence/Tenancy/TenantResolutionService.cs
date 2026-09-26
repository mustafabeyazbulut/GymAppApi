using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Tenancy;

public class TenantResolutionService : ITenantResolutionService
{
    private readonly GymAppApiDbContext _dbContext;

    public TenantResolutionService(GymAppApiDbContext dbContext) => _dbContext = dbContext;

    public async Task<ResolvedTenant> ResolveForUserAsync(
        int userId,
        int? preferredCompanyId = null,
        int? activeAssignmentId = null,
        CancellationToken cancellationToken = default)
    {
        // One of the few places allowed to bypass the tenant query filter (see
        // also GetMeQueryHandler, same rationale) — resolving a user's OWN
        // assignments must not itself already be tenant-filtered.
        // Company: firmanın aktifliği bağlama taşınır (CompanyInactive) - filtre
        // IgnoreQueryFilters ile include'a da uygulanmaz, pasif firma da yüklenir.
        // AsNoTracking: istek boyunca aynı (scoped) DbContext'i kullanan
        // handler'ların Company/Assignment güncellemeleriyle izleme çakışmasın.
        var assignments = await _dbContext.Assignments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(a => a.Company)
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync(cancellationToken);

        var staffAssignments = assignments.Where(a => IsStaffRole(a.Role)).ToList();

        // 1) Açık seçim (X-Active-Assignment-Id): bağlam tamamen o atamadan
        //    kurulur. Atama çağırana ait, aktif ve bir personel ataması
        //    olmalı - değilse reddedilir (sessizce başka bir atamaya düşmek,
        //    mobilin yanlış kapsamda işlem yapmasına yol açardı). SuperAdmin
        //    de bir personel ataması seçtiyse o atama olarak hareket eder
        //    (senaryo §4.6), platform geneli bypass ile değil.
        if (activeAssignmentId is not null)
        {
            var selected = staffAssignments.FirstOrDefault(a => a.Id == activeAssignmentId);
            return selected is null
                ? new ResolvedTenant(false, null, null, ActiveAssignmentRejected: true)
                : FromAssignment(selected);
        }

        if (assignments.Count == 0)
        {
            return new ResolvedTenant(false, null, null);
        }

        // 2) Header yok, SuperAdmin: rolü SuperAdmin, ama tenant filtrelerini
        //    kapatan platform bypass'ı YOK (IsSuperAdmin=false, senaryo §10.6) -
        //    Sistem Sahibi gym verisini göremez. Firma yönetimi uç noktaları
        //    (SuperAdminOnly) kendi okumalarında IgnoreQueryFilters kullanır.
        //    ITenantContext.IsSuperAdmin artık sadece sistem işleri (ör.
        //    hatırlatma job'ları) içindir.
        if (assignments.Any(a => a.Role == AssignmentRole.SuperAdmin))
        {
            return new ResolvedTenant(false, null, null, Role: AssignmentRole.SuperAdmin);
        }

        // 3) Header yok, personel ataması var - belirleyici seçim kuralı:
        //    - Eski X-Active-Company-Id ile eşleşen atamalar varsa sadece
        //      onlar arasından seçilir (geriye dönük uyumluluk).
        //    - Rol önceliği: GymAdmin > BranchManager > Trainer.
        //    - Eşitlikte en küçük Id (en eski atama).
        //    Tek personel ataması olan (yaygın durum) çağıran için bu doğal
        //    olarak o tek atamadır.
        if (staffAssignments.Count > 0)
        {
            var candidates = preferredCompanyId is not null && staffAssignments.Any(a => a.CompanyId == preferredCompanyId)
                ? staffAssignments.Where(a => a.CompanyId == preferredCompanyId)
                : staffAssignments;
            var chosen = candidates.OrderBy(a => RolePriority(a.Role)).ThenBy(a => a.Id).First();
            return FromAssignment(chosen);
        }

        // Personel ataması yok (ör. sadece paketli üye): personel bağlamı yok.
        return new ResolvedTenant(false, null, null);
    }

    private static bool IsStaffRole(AssignmentRole role) =>
        role is AssignmentRole.GymAdmin or AssignmentRole.BranchManager or AssignmentRole.Trainer;

    private static int RolePriority(AssignmentRole role) => role switch
    {
        AssignmentRole.GymAdmin => 0,
        AssignmentRole.BranchManager => 1,
        AssignmentRole.Trainer => 2,
        _ => 3,
    };

    private static ResolvedTenant FromAssignment(Assignment assignment) =>
        new(false, assignment.CompanyId, assignment.BranchId, assignment.Id, assignment.Role,
            CompanyInactive: assignment.Company is { IsActive: false });
}
