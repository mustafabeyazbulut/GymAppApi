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
public static class AssignmentInvitationAcceptance
{
    public static async Task<Assignment> AcceptAsync(IUnitOfWork unitOfWork, PendingAssignmentInvitation invitation, CancellationToken cancellationToken)
    {
        // Davet her durumda tüketilir - hata olsa bile ikinci kez kullanılamaz.
        invitation.IsUsed = true;
        unitOfWork.GetWriteRepository<PendingAssignmentInvitation>().Update(invitation);

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
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
                await unitOfWork.SaveChangesAsync(cancellationToken);
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
