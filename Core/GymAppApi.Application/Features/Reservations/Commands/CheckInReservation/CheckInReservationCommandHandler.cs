using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CheckInReservation;

// Staff/the reservation's own Trainer only - same authorization shape as
// MarkReservationNoShowCommandHandler.
public class CheckInReservationCommandHandler : IRequestHandler<CheckInReservationCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public CheckInReservationCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(CheckInReservationCommand request, CancellationToken cancellationToken)
    {
        var reservation = await _unitOfWork.GetReadRepository<Reservation>()
            .GetAsync(r => r.Id == request.ReservationId, cancellationToken: cancellationToken);
        if (reservation is null)
        {
            throw new NotFoundException("ReservationNotFound", request.ReservationId);
        }

        bool callerIsAuthorized;
        if (reservation.TrainerId == request.RequestedByUserId)
        {
            callerIsAuthorized = true;
        }
        else
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            callerIsAuthorized = callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == reservation.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == reservation.BranchId));
        }
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCheckIn");
        }

        if (reservation.Status != ReservationStatus.Booked)
        {
            throw new ReservationNotBookedException();
        }

        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == reservation.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null || assignment.RemainingSessions is null or <= 0)
        {
            throw new NoRemainingSessionsException();
        }

        assignment.RemainingSessions -= 1;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);

        reservation.Status = ReservationStatus.CheckedIn;
        _unitOfWork.GetWriteRepository<Reservation>().Update(reservation);

        var checkIn = new CheckIn
        {
            PackageAssignmentId = reservation.PackageAssignmentId,
            ReservationId = reservation.Id,
            CompanyId = reservation.CompanyId,
            BranchId = reservation.BranchId,
            CheckedInAt = DateTime.UtcNow,
            RecordedByUserId = request.RequestedByUserId,
        };
        await _unitOfWork.GetWriteRepository<CheckIn>().AddAsync(checkIn, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
