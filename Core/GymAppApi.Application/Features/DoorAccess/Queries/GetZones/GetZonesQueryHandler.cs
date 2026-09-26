using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.DoorAccess.Queries.GetZones;

// StaffManagement policy'siyle korunuyor - ambient tenant context'i her
// zaman kendi firmasına daralttığı için (GymAdmin/BranchManager gerçek bir
// Assignment'a sahip), ClassSession sorgularının aksine IgnoreQueryFilters
// gerekmiyor; sadece hedef şubenin çağıranın yetkisi dahilinde olduğu
// TEKRAR kontrol ediliyor.
public class GetZonesQueryHandler : IRequestHandler<GetZonesQuery, IReadOnlyList<ZoneDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetZonesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ZoneDto>> Handle(GetZonesQuery request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == request.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        var zones = await _unitOfWork.GetReadRepository<Zone>().GetAllAsync(
            z => z.BranchId == request.BranchId,
            include: q => q.IgnoreQueryFilters().Include(z => z.Branch),
            cancellationToken: cancellationToken);

        return zones.Select(z => new ZoneDto { Id = z.Id, BranchId = z.BranchId, Name = z.Name }).ToList();
    }
}
