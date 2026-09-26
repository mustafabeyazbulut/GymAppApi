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
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public AddStaffMemberCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<AddStaffMemberCommandResult> Handle(AddStaffMemberCommand request, CancellationToken cancellationToken)
    {
        // Telefon her zaman kanonik E.164 olarak aranır/saklanır/SMS'e verilir -
        // validator ValidPhoneNumber ile geçerliliği zaten garanti ediyor.
        var phone = _phoneNumberNormalizer.NormalizeIfPhone(request.Phone);

        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
        }

        // The [Authorize] policy only proves the caller's active role - re-check
        // it's scoped to THIS branch's company (GymAdmin) or THIS exact branch
        // (BranchManager). Sistem Sahibi personel ekleyemez (senaryo §10.6).
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        // A BranchManager may add Trainers to their own branch, but must
        // never be able to create peer/other BranchManagers - only GymAdmin
        // of this company can assign that role.
        var callerIsAuthorized = request.Role == AssignmentRole.BranchManager
            ? callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId))
            : callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenAddStaffToBranch");
        }

        // Never creates a new User — staff attach an already-registered
        // person to a branch, they don't mint accounts by phone. See
        // .claude/memory/feedback-never-remove-registration-pointer.md.
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == phone, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("PhoneNotRegistered", phone);
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

        // GymAdmin already covers every branch of the company; a BranchManager
        // assignment on top of that is redundant/conflicting and is blocked -
        // same rule enforced the other way in InviteGymAdminCommandHandler.
        if (request.Role == AssignmentRole.BranchManager)
        {
            var targetIsAlreadyGymAdminOfThisCompany = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
                a => a.UserId == user.Id && a.CompanyId == branch.CompanyId &&
                     a.Role == AssignmentRole.GymAdmin && a.IsActive, cancellationToken);
            if (targetIsAlreadyGymAdminOfThisCompany)
            {
                throw new ConflictingAssignmentRoleException();
            }
        }

        // Security requirement: the caller knowing this phone number is
        // never enough by itself to attach the person - the Assignment only
        // comes into existence once the invitee confirms this code
        // themselves (ConfirmAssignmentInvitationCommand).
        var code = await AssignmentInvitationService.IssueAsync(
            _unitOfWork, user.Id, branch.CompanyId, branch.Id, request.Role, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            phone,
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
