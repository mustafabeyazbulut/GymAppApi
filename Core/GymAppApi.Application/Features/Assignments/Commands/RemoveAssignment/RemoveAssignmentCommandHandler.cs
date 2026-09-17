using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;

public class RemoveAssignmentCommandHandler : IRequestHandler<RemoveAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationSender _pushNotificationSender;

    public RemoveAssignmentCommandHandler(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task Handle(RemoveAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignmentReadRepo = _unitOfWork.GetReadRepository<Assignment>();
        var assignment = await assignmentReadRepo.GetAsync(
            a => a.Id == request.AssignmentId && a.IsActive, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Atama {request.AssignmentId} bulunamadı.");
        }

        var callerAssignments = await assignmentReadRepo.GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // Mirrors exactly who may ADD each role (Tasks 2-3, 5): a
        // BranchManager may remove a Trainer from their own branch, but
        // never a peer BranchManager or a GymAdmin; only GymAdmin(of this
        // company)/SuperAdmin may remove a BranchManager or a peer GymAdmin.
        var callerIsAuthorized = assignment.Role switch
        {
            AssignmentRole.GymAdmin or AssignmentRole.BranchManager => callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId)),
            AssignmentRole.Trainer => callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId)),
            _ => false,
        };
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu atamayı kaldırma yetkiniz yok.");
        }

        // A company must always keep at least one active GymAdmin - unless
        // SuperAdmin is the one removing it (the explicit platform-level
        // override the user asked for, e.g. to force a replacement later).
        if (assignment.Role == AssignmentRole.GymAdmin)
        {
            var callerIsSuperAdmin = callerAssignments.Any(a => a.Role == AssignmentRole.SuperAdmin);
            if (!callerIsSuperAdmin)
            {
                var otherActiveGymAdminExists = await assignmentReadRepo.AnyAsync(
                    a => a.CompanyId == assignment.CompanyId && a.BranchId == null &&
                         a.Role == AssignmentRole.GymAdmin && a.IsActive && a.Id != assignment.Id, cancellationToken);
                if (!otherActiveGymAdminExists)
                {
                    throw new LastGymAdminException();
                }
            }
        }

        assignment.IsActive = false;
        _unitOfWork.GetWriteRepository<Assignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, assignment.UserId,
            "Atama kaldırıldı",
            $"GymApp'teki {assignment.Role} atamanız kaldırıldı.",
            cancellationToken);
    }
}
