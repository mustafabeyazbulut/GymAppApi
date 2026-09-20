using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.DeleteZone;

public class DeleteZoneCommand : IRequest
{
    // Route segmentinden controller tarafından set edilir.
    public int ZoneId { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
