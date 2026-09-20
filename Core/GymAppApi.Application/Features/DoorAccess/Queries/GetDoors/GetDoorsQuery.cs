using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Queries.GetDoors;

public class GetDoorsQuery : IRequest<IReadOnlyList<DoorDto>>
{
    public int ZoneId { get; set; }
    public int RequestedByUserId { get; set; }
}
