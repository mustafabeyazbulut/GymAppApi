using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateZone;

public class CreateZoneCommand : IRequest<CreateZoneCommandResult>
{
    public int BranchId { get; set; }
    public string Name { get; set; } = null!;

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
