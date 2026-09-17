using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
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

        var alreadyAssignedInCompany = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == user.Id && a.CompanyId == branch.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssignedInCompany)
        {
            throw new UserAlreadyAssignedException();
        }

        var assignment = new Assignment
        {
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = request.Role,
            IsActive = true,
        };
        await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var notificationText = $"GymApp'te bir şubeye {request.Role} olarak atandınız.";
        await _smsSender.SendAsync(request.Phone, notificationText, cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, user.Id, "Yeni şube ataması", notificationText, cancellationToken);

        return new AddStaffMemberCommandResult
        {
            AssignmentId = assignment.Id,
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = assignment.Role.ToString(),
        };
    }
}
