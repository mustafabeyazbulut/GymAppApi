using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Invitations.Exceptions;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Invitations;

// Davet gönderildiği anda geçerli olan hedef (firma, şube, paket) kabul
// anında hâlâ aktif olmalı - kapatılmış şubeye personel ya da pasif pakete
// üye eklenmesin. Kabulün transaction'ı içinde çağrılır; hata her şeyi geri
// alır ve davet yanmaz.
//
// IgnoreQueryFilters: davetli genellikle o firmada bağlamı olmayan biri;
// ayrıca filtreler pasif satırları zaten gizler - "pasif" ile "yok"u ayırt
// etmek için filtresiz okunur. Okumalar davetin kendi kimliklerine sabit.
public static class InvitationTargetGuard
{
    public static async Task EnsureActiveAsync(
        IUnitOfWork unitOfWork, int companyId, int? branchId, int? packageId, CancellationToken cancellationToken)
    {
        var company = await unitOfWork.GetReadRepository<Company>().GetAsync(
            c => c.Id == companyId,
            include: q => q.IgnoreQueryFilters().Include(c => c.Branches),
            cancellationToken: cancellationToken);
        if (company is null || !company.IsActive)
        {
            throw new InvitationTargetInactiveException();
        }

        if (branchId is not null)
        {
            var branch = await unitOfWork.GetReadRepository<Branch>().GetAsync(
                b => b.Id == branchId && b.CompanyId == companyId,
                include: q => q.IgnoreQueryFilters().Include(b => b.Company),
                cancellationToken: cancellationToken);
            if (branch is null || !branch.IsActive)
            {
                throw new InvitationTargetInactiveException();
            }
        }

        if (packageId is not null)
        {
            var package = await unitOfWork.GetReadRepository<Package>().GetAsync(
                p => p.Id == packageId,
                include: q => q.IgnoreQueryFilters().Include(p => p.Company),
                cancellationToken: cancellationToken);
            if (package is null || !package.IsActive)
            {
                throw new InvitationTargetInactiveException();
            }
        }
    }
}
