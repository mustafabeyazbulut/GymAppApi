using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateDoor;

public class CreateDoorCommand : IRequest<CreateDoorCommandResult>
{
    // Route segmentinden controller tarafından set edilir.
    public int ZoneId { get; set; }
    public string Name { get; set; } = null!;

    public int RequestedByUserId { get; set; }
}
