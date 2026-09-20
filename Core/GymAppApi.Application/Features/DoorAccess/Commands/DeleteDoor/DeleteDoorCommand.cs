using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.DeleteDoor;

public class DeleteDoorCommand : IRequest
{
    public int DoorId { get; set; }
    public int RequestedByUserId { get; set; }
}
