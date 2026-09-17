using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;

public class ConfirmAssignmentInvitationCommandHandler : IRequestHandler<ConfirmAssignmentInvitationCommand, ConfirmAssignmentInvitationCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmAssignmentInvitationCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ConfirmAssignmentInvitationCommandResult> Handle(ConfirmAssignmentInvitationCommand request, CancellationToken cancellationToken)
    {
        var invitationWriteRepo = _unitOfWork.GetWriteRepository<PendingAssignmentInvitation>();
        var now = DateTime.UtcNow;

        // A user can have more than one live invitation at once (different
        // companies) - fetch all of them rather than assuming exactly one.
        var liveInvitations = await _unitOfWork.GetReadRepository<PendingAssignmentInvitation>().GetAllAsync(
            p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now, cancellationToken: cancellationToken);

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

        matching.IsUsed = true;
        invitationWriteRepo.Update(matching);

        // Defense in depth: something else (another invitation, another
        // route) could have assigned this user to the company in the
        // meantime. The invitation is still consumed either way - it must
        // never be redeemable twice.
        var alreadyAssigned = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == matching.TargetUserId && a.CompanyId == matching.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssigned)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new UserAlreadyAssignedException();
        }

        var assignment = new Assignment
        {
            UserId = matching.TargetUserId,
            CompanyId = matching.CompanyId,
            BranchId = matching.BranchId,
            Role = matching.Role,
            IsActive = true,
        };
        await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConfirmAssignmentInvitationCommandResult
        {
            AssignmentId = assignment.Id,
            CompanyId = assignment.CompanyId!.Value,
            BranchId = assignment.BranchId,
            Role = assignment.Role.ToString(),
        };
    }
}
