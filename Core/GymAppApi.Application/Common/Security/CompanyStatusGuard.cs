using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Security;

// Senaryo §7: firma pasife alınınca yeni işlem yapılamaz, geçmiş veri durur.
// Kaynağın (paket ataması, rezervasyon, ders, atama...) firması pasifse o
// kaynak üzerinde yapılan her yazma - personel VE üye - 403 CompanyInactive.
// Okumalar etkilenmez.
//
// Personel politikalı uçlar ayrıca WebApi ActiveCompanyRequiredFilter ile
// aktif bağlama göre erken durdurulur; sade [Authorize] olan karma uçlarda
// (üye + personel) bağlam yeterli değil (üyenin firma bağlamı yok), bu yüzden
// kontrol kaynağın kendi CompanyId'siyle handler'da yapılır. Yetki kontrolünden
// SONRA çağrılmalı - yetkisiz çağırana firmanın durumu sızmasın.
public static class CompanyStatusGuard
{
    public static async Task EnsureActiveAsync(IUnitOfWork unitOfWork, int companyId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Company'nin kendi filtresi pasif firmayı zaten
        // gizler - "pasif" ile "yok"u ayırt etmek için filtresiz okunur.
        var company = await unitOfWork.GetReadRepository<Company>().GetAsync(
            c => c.Id == companyId,
            include: q => q.IgnoreQueryFilters().Include(c => c.Branches),
            cancellationToken: cancellationToken);
        if (company is null || !company.IsActive)
        {
            throw new ForbiddenException("CompanyInactive");
        }
    }
}
