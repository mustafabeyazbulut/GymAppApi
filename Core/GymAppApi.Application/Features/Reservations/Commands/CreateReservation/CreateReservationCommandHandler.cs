using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reservations.Commands.CreateReservation;

public class CreateReservationCommandHandler : IRequestHandler<CreateReservationCommand, CreateReservationCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateReservationCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreateReservationCommandResult> Handle(CreateReservationCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: this endpoint is Member-callable (booking their
        // own package), and a plain Member has no Assignment row, so their
        // ambient CompanyId is always null - see the standing rule in
        // .claude/memory/project-member-package-linkage-design.md. Safe
        // because the explicit authorization check below is what gates
        // access, not the tenant filter.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Package),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        bool callerIsAuthorized;
        if (assignment.MemberUserId == request.RequestedByUserId)
        {
            callerIsAuthorized = true;
        }
        else
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            callerIsAuthorized = callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId) ||
                (a.Role == AssignmentRole.Trainer && a.BranchId == assignment.BranchId));
        }
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCreateReservation");
        }

        // Eligible only for an active, session-based assignment with sessions
        // left - RemainingSessions is null for Duration-type assignments.
        if (assignment.Status != PackageAssignmentStatus.Active || assignment.RemainingSessions is null or <= 0)
        {
            throw new PackageAssignmentNotEligibleForReservationException();
        }

        // GetAllAsync+IgnoreQueryFilters rather than AnyAsync: AnyAsync has no
        // way to bypass the tenant filter, and this check has the exact same
        // Member-caller-has-no-Assignment problem as the PackageAssignment
        // fetch above - without this, a conflicting Booked reservation from
        // another company's ambient-context mismatch would be invisible here,
        // silently allowing a double-booking.
        var conflicting = await _unitOfWork.GetReadRepository<Reservation>().GetAllAsync(
            r => r.TrainerId == request.TrainerId && r.ScheduledAt == request.ScheduledAt && r.Status == ReservationStatus.Booked,
            include: q => q.IgnoreQueryFilters().Include(r => r.PackageAssignment),
            cancellationToken: cancellationToken);
        if (conflicting.Count > 0)
        {
            throw new ReservationConflictException();
        }

        var reservation = new Reservation
        {
            PackageAssignmentId = assignment.Id,
            MemberUserId = assignment.MemberUserId,
            TrainerId = request.TrainerId,
            CompanyId = assignment.CompanyId,
            BranchId = assignment.BranchId,
            ScheduledAt = request.ScheduledAt,
            Status = ReservationStatus.Booked,
            CreatedByUserId = request.RequestedByUserId,
            QrCode = Random.Shared.Next(100000, 999999).ToString(),
        };
        await _unitOfWork.GetWriteRepository<Reservation>().AddAsync(reservation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateReservationCommandResult
        {
            Id = reservation.Id,
            ScheduledAt = reservation.ScheduledAt,
            QrCode = reservation.QrCode,
        };
    }
}
