using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentCheckIns;

public class GetPackageAssignmentCheckInsQueryHandler : IRequestHandler<GetPackageAssignmentCheckInsQuery, IReadOnlyList<CheckInDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageAssignmentCheckInsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<CheckInDto>> Handle(GetPackageAssignmentCheckInsQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters (here and below): Member-callable (their own
        // check-in history) - same standing rule as the Payments/Reservations
        // queries.
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
            // Trainer da dahil - bu şubede çalışan bir antrenör, kendi
            // eğittiği/edeceği bir üyenin check-in geçmişini (devam
            // durumunu) görebilmeli, aksi halde antrenör panelinde hiçbir
            // katılım verisi gösterilemez.
            var callerIsAuthorized = callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId) ||
                (a.Role == AssignmentRole.Trainer && a.BranchId == assignment.BranchId));
            if (!callerIsAuthorized)
            {
                throw new ForbiddenException("ForbiddenViewCheckIns");
            }
        }

        var checkIns = await _unitOfWork.GetReadRepository<CheckIn>().GetAllAsync(
            c => c.PackageAssignmentId == assignment.Id,
            include: q => q.IgnoreQueryFilters().Include(c => c.PackageAssignment),
            cancellationToken: cancellationToken);

        return checkIns
            .Select(c => new CheckInDto
            {
                Id = c.Id,
                ReservationId = c.ReservationId,
                CheckedInAt = c.CheckedInAt,
            })
            .ToList();
    }
}
