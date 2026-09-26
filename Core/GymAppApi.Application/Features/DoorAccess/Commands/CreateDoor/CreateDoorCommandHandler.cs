using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateDoor;

public class CreateDoorCommandHandler : IRequestHandler<CreateDoorCommand, CreateDoorCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateDoorCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreateDoorCommandResult> Handle(CreateDoorCommand request, CancellationToken cancellationToken)
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
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == zone.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        var door = new Door { ZoneId = zone.Id, CompanyId = zone.CompanyId, Name = request.Name };
        await _unitOfWork.GetWriteRepository<Door>().AddAsync(door, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateDoorCommandResult { Id = door.Id, Name = door.Name };
    }
}
