using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordGeneralCheckIn;

// Walk-in / no-reservation check-in - front desk only (GymAdmin/BranchManager;
// no SuperAdmin, no Trainer, no Member self-service), unlike the reservation-
// based check-in commands which the reservation's own Trainer may also do.
public class RecordGeneralCheckInCommandHandler : IRequestHandler<RecordGeneralCheckInCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public RecordGeneralCheckInCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(RecordGeneralCheckInCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCheckIn");
        }

        // RemainingSessions is null for a Duration-type assignment - no
        // decrement, unlimited entries for the membership's duration. It's a
        // set number for a SessionBased assignment, and must be > 0.
        if (assignment.RemainingSessions is not null)
        {
            if (assignment.RemainingSessions <= 0)
            {
                throw new NoRemainingSessionsException();
            }
            assignment.RemainingSessions -= 1;
            _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        }

        var checkIn = new CheckIn
        {
            PackageAssignmentId = assignment.Id,
            ReservationId = null,
            CompanyId = assignment.CompanyId,
            BranchId = assignment.BranchId,
            CheckedInAt = DateTime.UtcNow,
            RecordedByUserId = request.RequestedByUserId,
        };
        await _unitOfWork.GetWriteRepository<CheckIn>().AddAsync(checkIn, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
