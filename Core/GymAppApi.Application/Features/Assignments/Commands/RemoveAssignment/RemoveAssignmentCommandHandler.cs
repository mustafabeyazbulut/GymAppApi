using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
        // IgnoreQueryFilters: Sistem Sahibi'nin tenant bağlamı yok (senaryo §10.6,
        // platform bypass'ı kaldırıldı) - firma yönetimi kapsamında bir GymAdmin
        // atamasını görebilmesi için. Güvenli: yetki aşağıda açıkça kontrol ediliyor.
        var assignment = await assignmentReadRepo.GetAsync(
            a => a.Id == request.AssignmentId && a.IsActive,
            include: q => q.IgnoreQueryFilters().Include(a => a.User),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("AssignmentNotFound", request.AssignmentId);
        }

        var callerAssignments = await assignmentReadRepo.GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // Mirrors exactly who may ADD each role: a BranchManager may remove a
        // Trainer from their own branch, but never a peer BranchManager or a
        // GymAdmin; only a GymAdmin of this company may remove a BranchManager.
        // Sistem Sahibi gym personeline karışmaz (senaryo §10.6) - sadece
        // firma yönetiminin parçası olarak GymAdmin atamasını kaldırabilir.
        var callerIsAuthorized = assignment.Role switch
        {
            AssignmentRole.GymAdmin => callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId)),
            AssignmentRole.BranchManager => callerAssignments.Any(a =>
                a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId),
            AssignmentRole.Trainer => callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId)),
            _ => false,
        };
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenRemoveAssignment");
        }

        // Self-removal (a GymAdmin/BranchManager removing their own assignment)
        // is intentionally allowed - it is just "stepping down" - and remains
        // subject to the same last-GymAdmin protection below, so it is safe by
        // construction rather than an oversight.
        // A company must always keep at least one active GymAdmin (senaryo §4.5) -
        // Sistem Sahibi dahil, istisna yok. Yerine yeni biri gerekiyorsa önce
        // yenisi davet edilip onaylanır, sonra eskisi kaldırılır.
        // GetAllAsync + IgnoreQueryFilters: AnyAsync'in filtre kaçışı yok ve
        // Sistem Sahibi'nin tenant bağlamı olmadığı için filtreli sorgu diğer
        // Gym Admin'leri göremez, her zaman "yok" derdi.
        if (assignment.Role == AssignmentRole.GymAdmin)
        {
            var otherActiveGymAdmins = await assignmentReadRepo.GetAllAsync(
                a => a.CompanyId == assignment.CompanyId && a.BranchId == null &&
                     a.Role == AssignmentRole.GymAdmin && a.IsActive && a.Id != assignment.Id,
                include: q => q.IgnoreQueryFilters().Include(a => a.User),
                cancellationToken: cancellationToken);
            if (otherActiveGymAdmins.Count == 0)
            {
                throw new LastGymAdminException();
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
