using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateZone;

public class CreateZoneCommandHandler : IRequestHandler<CreateZoneCommand, CreateZoneCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateZoneCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreateZoneCommandResult> Handle(CreateZoneCommand request, CancellationToken cancellationToken)
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

        var zone = new Zone
        {
            CompanyId = branch.CompanyId,
            BranchId = request.BranchId,
            Name = request.Name,
            CreatedByUserId = request.RequestedByUserId,
        };
        await _unitOfWork.GetWriteRepository<Zone>().AddAsync(zone, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateZoneCommandResult { Id = zone.Id, Name = zone.Name };
    }
}
