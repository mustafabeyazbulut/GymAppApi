using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommandHandler : IRequestHandler<InviteGymAdminCommand, InviteGymAdminCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public InviteGymAdminCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<InviteGymAdminCommandResult> Handle(InviteGymAdminCommand request, CancellationToken cancellationToken)
    {
        // Telefon her zaman kanonik E.164 olarak aranır/saklanır/SMS'e verilir -
        // validator ValidPhoneNumber ile geçerliliği zaten garanti ediyor.
        var phone = _phoneNumberNormalizer.NormalizeIfPhone(request.Phone);

        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException("CompanyNotFound", request.CompanyId);
        }

        // Same pattern as every other mutation here: the [Authorize] policy
        // only proves the caller holds SOME GymAdmin/SuperAdmin assignment
        // somewhere - re-check it's scoped to THIS company.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenInviteGymAdmin");
        }

        // Never creates a new User - same rule as CreateCompanyCommand. See
        // .claude/memory/feedback-never-remove-registration-pointer.md.
        var invitedUser = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == phone, cancellationToken: cancellationToken);
        if (invitedUser is null)
        {
            throw new NotFoundException("PhoneNotRegistered", phone);
        }

        var alreadyGymAdminOfThisCompany = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == invitedUser.Id && a.CompanyId == request.CompanyId && a.BranchId == null &&
                 a.Role == AssignmentRole.GymAdmin && a.IsActive, cancellationToken);
        if (alreadyGymAdminOfThisCompany)
        {
            throw new UserAlreadyAssignedException();
        }

        // GymAdmin already covers every branch of the company; a pre-existing
        // BranchManager assignment there is redundant/conflicting and blocks
        // the invite - same rule enforced the other way in AddStaffMemberCommandHandler.
        var inviteeIsAlreadyBranchManagerOfThisCompany = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == invitedUser.Id && a.CompanyId == request.CompanyId &&
                 a.Role == AssignmentRole.BranchManager && a.IsActive, cancellationToken);
        if (inviteeIsAlreadyBranchManagerOfThisCompany)
        {
            throw new ConflictingAssignmentRoleException();
        }

        // Security requirement: knowing this phone number is never enough by
        // itself - the Assignment only comes into existence once the
        // invitee confirms this code themselves (POST /api/assignments/confirm).
        var code = await AssignmentInvitationService.IssueAsync(
            _unitOfWork, invitedUser.Id, request.CompanyId, null, AssignmentRole.GymAdmin, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            phone,
            $"GymApp'te '{company.Name}' firmasının Gym Admin'i olmak üzeresiniz. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, invitedUser.Id,
            "Yeni firma daveti",
            "Bir firmanın Gym Admin'i olmanız için davet gönderildi. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new InviteGymAdminCommandResult
        {
            UserId = invitedUser.Id,
            CompanyId = request.CompanyId,
        };
    }
}
