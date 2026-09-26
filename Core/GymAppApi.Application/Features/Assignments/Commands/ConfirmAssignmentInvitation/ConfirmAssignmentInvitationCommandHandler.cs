using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;

// SMS koduyla personel davetini onaylama. Kabulün iş kuralları (duplicate,
// GymAdmin+BranchManager çakışması, atamanın oluşturulması) uygulama içi
// davet kabulüyle ortak: AssignmentInvitationAcceptance.
public class ConfirmAssignmentInvitationCommandHandler : IRequestHandler<ConfirmAssignmentInvitationCommand, ConfirmAssignmentInvitationCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmAssignmentInvitationCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ConfirmAssignmentInvitationCommandResult> Handle(ConfirmAssignmentInvitationCommand request, CancellationToken cancellationToken)
    {
        var invitationWriteRepo = _unitOfWork.GetWriteRepository<PendingAssignmentInvitation>();
        var now = DateTime.UtcNow;
        // Davet 7 gün yaşar (uygulama içi kabul için), ama SMS KODU sadece
        // gönderildikten sonraki kısa pencerede geçerlidir.
        var codeIssuedAfter = now.AddMinutes(-AssignmentInvitationService.CodeValidityMinutes);

        // A user can have more than one live invitation at once (different
        // companies) - fetch all of them rather than assuming exactly one.
        var liveInvitations = await _unitOfWork.GetReadRepository<PendingAssignmentInvitation>().GetAllAsync(
            p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now && p.CreatedAt > codeIssuedAfter,
            cancellationToken: cancellationToken);

        var matching = liveInvitations.FirstOrDefault(p => p.Code == request.Code && p.AttemptCount < AssignmentInvitationService.MaxAttempts);
        if (matching is null)
        {
            // We don't know which invitation the caller meant, so a wrong
            // guess burns an attempt against every one of their still-live
            // invitations rather than being free just because there happen
            // to be several (or none) to blame it on.
            foreach (var invitation in liveInvitations.Where(p => p.AttemptCount < AssignmentInvitationService.MaxAttempts))
            {
                invitation.AttemptCount += 1;
                invitationWriteRepo.Update(invitation);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidAssignmentInvitationCodeException();
        }

        var assignment = await AssignmentInvitationAcceptance.AcceptAsync(_unitOfWork, matching, cancellationToken);

        return new ConfirmAssignmentInvitationCommandResult
        {
            AssignmentId = assignment.Id,
            CompanyId = assignment.CompanyId!.Value,
            BranchId = assignment.BranchId,
            Role = assignment.Role.ToString(),
        };
    }
}
