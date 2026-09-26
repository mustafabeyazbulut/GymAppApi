using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Invitations;

// Paket davetini kabul etmenin TEK uygulaması - hem SMS kodlu
// ConfirmPackageAssignment hem uygulama içi davet kabulü bunu çağırır.
// Çağıran daveti bulup sahipliğini/geçerliliğini doğrulamış olmalıdır.
public static class PackageInvitationAcceptance
{
    public static async Task<PackageAssignment> AcceptAsync(
        IUnitOfWork unitOfWork, PendingPackageAssignmentInvitation invitation, DateTime now, CancellationToken cancellationToken)
    {
        // Davet her durumda tüketilir - hata olsa bile ikinci kez kullanılamaz.
        // Atomik talep: eşzamanlı ikinci onay burada durur (bkz.
        // AssignmentInvitationAcceptance.ClaimAsync).
        invitation.IsUsed = true;
        await AssignmentInvitationAcceptance.ClaimAsync(unitOfWork, invitation, invitation.Id, cancellationToken);

        // IgnoreQueryFilters (bu metottaki tüm okumalar): kabul eden üyenin
        // genellikle hiçbir personel ataması yok, ambient CompanyId'si null -
        // filtreli okuma kendi paketlerini ve paket şablonunu ondan gizlerdi.
        // Sorgular üyenin kendi satırlarına / davetin PackageId'sine sabit.
        //
        // Engel sadece bu pakette şu an GEÇERLİ bir atama - süresi dolmuş,
        // hakkı bitmiş veya iptal edilmiş eski atama yenilemeyi engellemez.
        var membersValidAssignments = await unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.UsableOwnedBy(invitation.TargetUserId, now),
            include: q => q.IgnoreQueryFilters().Include(pa => pa.Package),
            cancellationToken: cancellationToken);
        if (membersValidAssignments.Any(pa => pa.PackageId == invitation.PackageId))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new MemberAlreadyHasThisPackageException();
        }

        var package = await unitOfWork.GetReadRepository<Package>().GetAsync(
            p => p.Id == invitation.PackageId,
            include: q => q.IgnoreQueryFilters().Include(p => p.Company),
            cancellationToken: cancellationToken);

        var assignment = new PackageAssignment
        {
            PackageId = invitation.PackageId,
            MemberUserId = invitation.TargetUserId,
            CompanyId = invitation.CompanyId,
            BranchId = invitation.BranchId,
            AssignedByUserId = invitation.RequestedByUserId,
            StartDate = now,
            EndDate = package?.DurationDays is int days ? now.AddDays(days) : null,
            RemainingSessions = package?.SessionCount,
            Status = PackageAssignmentStatus.Active,
        };
        await unitOfWork.GetWriteRepository<PackageAssignment>().AddAsync(assignment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }
}
