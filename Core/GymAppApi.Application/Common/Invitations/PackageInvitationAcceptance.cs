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
// Atomiklik: claim + paket ataması tek transaction içinde; her hata geri
// alınır ve davet yanmaz (gerekçe için bkz. AssignmentInvitationAcceptance).
public static class PackageInvitationAcceptance
{
    public static Task<PackageAssignment> AcceptAsync(
        IUnitOfWork unitOfWork, PendingPackageAssignmentInvitation invitation, DateTime now, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var assignment = await AcceptWithinTransactionAsync(unitOfWork, invitation, now, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                return assignment;
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });

    private static async Task<PackageAssignment> AcceptWithinTransactionAsync(
        IUnitOfWork unitOfWork, PendingPackageAssignmentInvitation invitation, DateTime now, CancellationToken cancellationToken)
    {
        // Atomik talep: eşzamanlı ikinci onay burada durur (InvitationWrites.ClaimAsync).
        invitation.IsUsed = true;
        await InvitationWrites.ClaimAsync(unitOfWork, invitation, invitation.Id, cancellationToken);

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
