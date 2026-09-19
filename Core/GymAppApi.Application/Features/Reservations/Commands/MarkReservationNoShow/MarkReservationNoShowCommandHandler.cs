using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.MarkReservationNoShow;

// Staff/the reservation's own Trainer only - unlike Cancel, the Member
// cannot mark their own reservation as a no-show.
public class MarkReservationNoShowCommandHandler : IRequestHandler<MarkReservationNoShowCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public MarkReservationNoShowCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(MarkReservationNoShowCommand request, CancellationToken cancellationToken)
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
            throw new ForbiddenException("ForbiddenMarkNoShow");
        }

        if (reservation.Status != ReservationStatus.Booked)
        {
            throw new ReservationNotBookedException();
        }

        reservation.Status = ReservationStatus.NoShow;
        _unitOfWork.GetWriteRepository<Reservation>().Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
