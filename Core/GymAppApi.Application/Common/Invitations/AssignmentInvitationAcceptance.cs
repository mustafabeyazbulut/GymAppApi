using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Invitations;

// Personel davetini (GymAdmin/BranchManager/Trainer) kabul etmenin TEK
// uygulaması - hem SMS kodlu ConfirmAssignmentInvitation hem uygulama içi
// /api/invitations/{type}/{id}/accept bunu çağırır, iş kuralları iki yerde
// tekrarlanmaz. Çağıran daveti bulup sahipliğini/geçerliliğini doğrulamış
// olmalıdır; bu metot kabulün kendisini yapar.
//
// Atomiklik: davetin talep edilmesi (claim) ile atamanın oluşturulması TEK
// transaction içindedir. Herhangi bir hata - kural ihlali (409 duplicate /
// rol çakışması) ya da beklenmeyen bir DB hatası - her şeyi geri alır: davet
// yanmaz, kullanıcı durumunu düzeltip tekrar deneyebilir. Transaction burada
// (komut seviyesinde ITransactionalRequest değil) çünkü SMS kodlu confirm'in
// yanlış kod yolu deneme sayacını transaction DIŞINDA kalıcılaştırmak zorunda.
// Claim'in xmin token'ı transaction içinde de eşzamanlı onayları serileştirir.
public static class AssignmentInvitationAcceptance
{
    public static Task<Assignment> AcceptAsync(IUnitOfWork unitOfWork, PendingAssignmentInvitation invitation, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var assignment = await AcceptWithinTransactionAsync(unitOfWork, invitation, cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                return assignment;
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });

    private static async Task<Assignment> AcceptWithinTransactionAsync(IUnitOfWork unitOfWork, PendingAssignmentInvitation invitation, CancellationToken cancellationToken)
    {
        // Davet sonrası kapatılmış şube/firma: 409, davet yanmaz.
        await InvitationTargetGuard.EnsureActiveAsync(unitOfWork, invitation.CompanyId, invitation.BranchId, packageId: null, cancellationToken);

        // Atomik talep: eşzamanlı ikinci onay burada durur (InvitationWrites.ClaimAsync).
        invitation.IsUsed = true;
        await InvitationWrites.ClaimAsync(unitOfWork, invitation, invitation.Id, cancellationToken);

        // IgnoreQueryFilters: kabul eden davetli, o firmada henüz hiçbir
        // bağlamı olmayan bir kullanıcı olabilir - filtreli okuma (eski
        // AnyAsync) onun o firmadaki mevcut atamalarını göremez ve aşağıdaki
        // iki kontrolü sessizce etkisiz bırakırdı. Sorgu davetlinin kendi
        // satırlarına sabitlendiği için sızıntı riski yok.
        var inviteesAssignmentsInCompany = await unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == invitation.TargetUserId && a.CompanyId == invitation.CompanyId && a.IsActive,
            include: q => q.IgnoreQueryFilters().Include(a => a.User),
            cancellationToken: cancellationToken);

        // Savunma: başka bir davet/yol bu arada aynı atamayı oluşturmuş olabilir.
        if (inviteesAssignmentsInCompany.Any(a => a.BranchId == invitation.BranchId && a.Role == invitation.Role))
        {
            throw new UserAlreadyAssignedException();
        }

        // GymAdmin firmanın tüm şubelerini kapsar - aynı firmada GymAdmin ile
        // BranchManager aynı kişide birlikte olamaz (davet anındaki kontrolün
        // yarış penceresini kapatır).
        if (invitation.Role is AssignmentRole.GymAdmin or AssignmentRole.BranchManager)
        {
            var conflictingRole = invitation.Role == AssignmentRole.GymAdmin ? AssignmentRole.BranchManager : AssignmentRole.GymAdmin;
            if (inviteesAssignmentsInCompany.Any(a => a.Role == conflictingRole))
            {
                throw new ConflictingAssignmentRoleException();
            }
        }

        var assignment = new Assignment
        {
            UserId = invitation.TargetUserId,
            CompanyId = invitation.CompanyId,
            BranchId = invitation.BranchId,
            Role = invitation.Role,
            IsActive = true,
        };
        await unitOfWork.GetWriteRepository<Assignment>().AddAsync(assignment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }
}
