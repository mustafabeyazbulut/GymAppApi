using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Queries.GetZones;

public class GetZonesQuery : IRequest<IReadOnlyList<ZoneDto>>
{
    public int BranchId { get; set; }
    public int RequestedByUserId { get; set; }
}
