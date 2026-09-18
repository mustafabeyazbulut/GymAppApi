using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reservations.Commands.CancelReservation;

public class CancelReservationCommandHandler : IRequestHandler<CancelReservationCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public CancelReservationCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(CancelReservationCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Member-callable (cancelling their own
        // reservation) - same standing rule as CreateReservationCommandHandler.
        var reservation = await _unitOfWork.GetReadRepository<Reservation>().GetAsync(
            r => r.Id == request.ReservationId,
            include: q => q.IgnoreQueryFilters().Include(r => r.PackageAssignment),
            cancellationToken: cancellationToken);
        if (reservation is null)
        {
            throw new NotFoundException($"Rezervasyon {request.ReservationId} bulunamadı.");
        }

        bool callerIsAuthorized;
        if (reservation.MemberUserId == request.RequestedByUserId || reservation.TrainerId == request.RequestedByUserId)
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
            throw new ForbiddenException("Bu rezervasyonu iptal etme yetkiniz yok.");
        }

        if (reservation.Status != ReservationStatus.Booked)
        {
            throw new ReservationNotBookedException();
        }

        reservation.Status = ReservationStatus.Cancelled;
        _unitOfWork.GetWriteRepository<Reservation>().Update(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
