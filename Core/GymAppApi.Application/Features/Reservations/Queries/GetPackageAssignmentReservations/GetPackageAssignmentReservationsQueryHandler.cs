using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentReservations;

public class GetPackageAssignmentReservationsQueryHandler : IRequestHandler<GetPackageAssignmentReservationsQuery, IReadOnlyList<ReservationDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageAssignmentReservationsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ReservationDto>> Handle(GetPackageAssignmentReservationsQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters (here and below): Member-callable (their own
        // reservations) - same standing rule as the Payments query.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Package),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        if (assignment.MemberUserId != request.RequestedByUserId)
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            // Trainer da dahil - CreateReservationCommandHandler'ın kendi
            // yetkilendirmesiyle aynı: bu şubedeki herhangi bir antrenör bir
            // rezervasyon oluşturabiliyorsa, aynı şekilde listeyi de
            // görebilmeli - aksi halde bir antrenör kendi oluşturduğu bir
            // rezervasyonu bile geriye dönük listeleyip göremez.
            var callerIsAuthorized = callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId) ||
                (a.Role == AssignmentRole.Trainer && a.BranchId == assignment.BranchId));
            if (!callerIsAuthorized)
            {
                throw new ForbiddenException("ForbiddenViewReservations");
            }
        }

        var reservations = await _unitOfWork.GetReadRepository<Reservation>().GetAllAsync(
            r => r.PackageAssignmentId == assignment.Id,
            include: q => q.IgnoreQueryFilters().Include(r => r.PackageAssignment),
            cancellationToken: cancellationToken);

        return reservations
            .Select(r => new ReservationDto
            {
                Id = r.Id,
                TrainerId = r.TrainerId,
                ScheduledAt = r.ScheduledAt,
                Status = r.Status.ToString(),
                QrCode = r.QrCode,
            })
            .ToList();
    }
}
