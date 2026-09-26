using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.DoorAccess.Commands.DeleteDoor;

public class DeleteDoorCommandHandler : IRequestHandler<DeleteDoorCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDoorCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(DeleteDoorCommand request, CancellationToken cancellationToken)
    {
        var door = await _unitOfWork.GetReadRepository<Door>().GetAsync(
            d => d.Id == request.DoorId,
            include: q => q.Include(d => d.Zone),
            cancellationToken: cancellationToken);
        if (door is null)
        {
            throw new NotFoundException("DoorNotFound", request.DoorId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == door.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        _unitOfWork.GetWriteRepository<Door>().Remove(door);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
