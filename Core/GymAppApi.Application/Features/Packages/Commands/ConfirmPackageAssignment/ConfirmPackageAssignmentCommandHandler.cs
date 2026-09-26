using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

// SMS koduyla paket davetini onaylama. Kabulün iş kuralları (geçerli paket
// engeli, atamanın oluşturulması) uygulama içi davet kabulüyle ortak:
// PackageInvitationAcceptance.
public class ConfirmPackageAssignmentCommandHandler : IRequestHandler<ConfirmPackageAssignmentCommand, ConfirmPackageAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmPackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ConfirmPackageAssignmentCommandResult> Handle(ConfirmPackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // Davet 7 gün yaşar (uygulama içi kabul için), ama SMS KODU sadece
        // gönderildikten sonraki kısa pencerede geçerlidir.
        var codeIssuedAfter = now.AddMinutes(-PackageAssignmentInvitationService.CodeValidityMinutes);

        Task<IReadOnlyList<PendingPackageAssignmentInvitation>> LoadLiveInvitationsAsync() =>
            _unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>().GetAllAsync(
                p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now && p.CreatedAt > codeIssuedAfter,
                cancellationToken: cancellationToken);
        var liveInvitations = await LoadLiveInvitationsAsync();

        var matching = liveInvitations.FirstOrDefault(p => p.Code == request.Code && p.AttemptCount < PackageAssignmentInvitationService.MaxAttempts);
        if (matching is null)
        {
            // Eşzamanlı yanlış tahminlerde artışlar kaybolmasın (InvitationWrites).
            await InvitationWrites.BurnAttemptsAsync(_unitOfWork, LoadLiveInvitationsAsync, invitation =>
            {
                if (invitation.AttemptCount >= PackageAssignmentInvitationService.MaxAttempts)
                {
                    return false;
                }
                invitation.AttemptCount += 1;
                return true;
            }, cancellationToken);
            throw new InvalidPackageAssignmentInvitationCodeException();
        }

        var assignment = await PackageInvitationAcceptance.AcceptAsync(_unitOfWork, matching, now, cancellationToken);

        return new ConfirmPackageAssignmentCommandResult
        {
            PackageAssignmentId = assignment.Id,
            PackageId = assignment.PackageId,
            EndDate = assignment.EndDate,
        };
    }
}
