using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Application.Common.Security;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CheckInReservationByCode;

// Same authorization/eligibility rules as CheckInReservationCommandHandler,
// just resolves the Reservation by its QrCode (still-Booked only) instead of
// by id - the front-desk/QR-scan entry point into the same check-in flow.
public class CheckInReservationByCodeCommandHandler : IRequestHandler<CheckInReservationByCodeCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public CheckInReservationByCodeCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(CheckInReservationByCodeCommand request, CancellationToken cancellationToken)
    {
        var matches = await _unitOfWork.GetReadRepository<Reservation>().GetAllAsync(
            r => r.QrCode == request.Code && r.Status == ReservationStatus.Booked, cancellationToken: cancellationToken);
        var reservation = matches.FirstOrDefault();
        if (reservation is null)
        {
            throw new InvalidReservationCodeException();
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
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == reservation.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == reservation.BranchId));
        }
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCheckIn");
        }

        await CompanyStatusGuard.EnsureActiveAsync(_unitOfWork, reservation.CompanyId, cancellationToken);

        // Yarış güvenliği: bkz. CheckInReservationCommandHandler - rezervasyon ve
        // paket ataması FOR UPDATE ile kilitlenip güncel durum yeniden kontrol edilir.
        reservation = await _unitOfWork.GetForUpdateAsync<Reservation>(reservation.Id, cancellationToken);
        if (reservation is null || reservation.Status != ReservationStatus.Booked)
        {
            throw new InvalidReservationCodeException();
        }

        var assignment = await _unitOfWork.GetForUpdateAsync<PackageAssignment>(reservation.PackageAssignmentId, cancellationToken);
        if (assignment is null || assignment.RemainingSessions is null or <= 0)
        {
            throw new NoRemainingSessionsException();
        }
        // Randevu alındıktan sonra paket süresi dolmuş/dondurulmuş/iptal olabilir.
        PackageAssignmentValidity.EnsureUsableForCheckIn(assignment, DateTime.UtcNow);

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
