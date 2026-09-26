using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Invitations.Common;

// Davet tipleri: listede dönen "type" ve kabul/red URL'sindeki {type}
// (büyük/küçük harf duyarsız).
public static class InvitationTypes
{
    public const string GymAdmin = "GymAdmin";
    public const string Staff = "Staff";
    public const string Package = "Package";

    public static string ForRole(AssignmentRole role) => role == AssignmentRole.GymAdmin ? GymAdmin : Staff;
}

// Kabul ve red uçlarının ortak davet bulma/doğrulama kuralları:
//   - Çağırana ait olmayan, bulunamayan, zaten kullanılmış (kabul/red
//     edilmiş) veya tipi URL'deki tiple uyuşmayan davet -> 404
//     InvitationNotFound (başkasının davetinin varlığı sızdırılmaz).
//   - Süresi dolmuş davet -> 410 InvitationExpired.
public static class InvitationLookup
{
    public static async Task<PendingAssignmentInvitation> FindAssignmentInvitationAsync(
        IUnitOfWork unitOfWork, string type, int invitationId, int userId, DateTime now, CancellationToken cancellationToken)
    {
        var invitation = await unitOfWork.GetReadRepository<PendingAssignmentInvitation>().GetAsync(
            p => p.Id == invitationId, enableTracking: true, cancellationToken: cancellationToken);
        if (invitation is null || invitation.TargetUserId != userId || invitation.IsUsed ||
            !string.Equals(InvitationTypes.ForRole(invitation.Role), type, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotFoundException("InvitationNotFound", invitationId);
        }

        if (invitation.ExpiresAt <= now)
        {
            throw new GoneException("InvitationExpired");
        }

        return invitation;
    }

    public static async Task<PendingPackageAssignmentInvitation> FindPackageInvitationAsync(
        IUnitOfWork unitOfWork, int invitationId, int userId, DateTime now, CancellationToken cancellationToken)
    {
        var invitation = await unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>().GetAsync(
            p => p.Id == invitationId, enableTracking: true, cancellationToken: cancellationToken);
        if (invitation is null || invitation.TargetUserId != userId || invitation.IsUsed)
        {
            throw new NotFoundException("InvitationNotFound", invitationId);
        }

        if (invitation.ExpiresAt <= now)
        {
            throw new GoneException("InvitationExpired");
        }

        return invitation;
    }

    public static bool IsPackage(string type) => string.Equals(type, InvitationTypes.Package, StringComparison.OrdinalIgnoreCase);

    public static bool IsAssignment(string type) =>
        string.Equals(type, InvitationTypes.GymAdmin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, InvitationTypes.Staff, StringComparison.OrdinalIgnoreCase);
}
