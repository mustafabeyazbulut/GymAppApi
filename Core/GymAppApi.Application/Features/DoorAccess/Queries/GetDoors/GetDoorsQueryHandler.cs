using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Queries.GetDoors;

public class GetDoorsQueryHandler : IRequestHandler<GetDoorsQuery, IReadOnlyList<DoorDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDoorsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<DoorDto>> Handle(GetDoorsQuery request, CancellationToken cancellationToken)
    {
        var zone = await _unitOfWork.GetReadRepository<Zone>()
            .GetAsync(z => z.Id == request.ZoneId, cancellationToken: cancellationToken);
        if (zone is null)
        {
            throw new NotFoundException("ZoneNotFound", request.ZoneId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == zone.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == zone.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        var doors = await _unitOfWork.GetReadRepository<Door>().GetAllAsync(
            d => d.ZoneId == request.ZoneId, cancellationToken: cancellationToken);

        return doors.Select(d => new DoorDto { Id = d.Id, ZoneId = d.ZoneId, Name = d.Name }).ToList();
    }
}
