using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandHandler : IRequestHandler<AddStaffMemberCommand, AddStaffMemberCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public AddStaffMemberCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<AddStaffMemberCommandResult> Handle(AddStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        // Same pattern as CreateAssignmentCommandHandler: the [Authorize]
        // policy only proves the caller holds SOME staff role somewhere -
        // re-check it's scoped to THIS branch's company (GymAdmin) or THIS
        // exact branch (BranchManager). SuperAdmin bypasses both checks.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeye üye/antrenör ekleme yetkiniz yok.");
        }

        // Never creates a new User — staff attach an already-registered
        // person to a branch, they don't mint accounts by phone. See
        // .claude/memory/feedback-never-remove-registration-pointer.md.
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.Phone, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"'{request.Phone}' numaralı kayıtlı bir kullanıcı bulunamadı.");
        }

        // Scoped to (CompanyId, BranchId, Role), not just CompanyId - a
        // Trainer/BranchManager can hold assignments at more than one branch
        // of the same company (and, separately, at any number of other
        // companies - that was never blocked). Only an exact duplicate
        // (same person, same branch, same role) is rejected.
        var alreadyHoldsThisExactAssignment = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == user.Id && a.CompanyId == branch.CompanyId && a.BranchId == branch.Id &&
                 a.Role == request.Role && a.IsActive, cancellationToken);
        if (alreadyHoldsThisExactAssignment)
        {
            throw new UserAlreadyAssignedException();
        }

        // Security requirement: the caller knowing this phone number is
        // never enough by itself to attach the person - the Assignment only
        // comes into existence once the invitee confirms this code
        // themselves (ConfirmAssignmentInvitationCommand).
        var code = await AssignmentInvitationService.IssueAsync(
            _unitOfWork, user.Id, branch.CompanyId, branch.Id, request.Role, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            request.Phone,
            $"GymApp'te bir şubeye {request.Role} olarak eklenmek üzeresiniz. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, user.Id,
            "Yeni şube daveti",
            "Bir şubeye eklenmeniz için davet gönderildi. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new AddStaffMemberCommandResult
        {
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = request.Role.ToString(),
        };
    }
}
