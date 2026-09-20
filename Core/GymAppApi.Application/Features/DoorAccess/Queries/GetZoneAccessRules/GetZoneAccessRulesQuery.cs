using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Queries.GetZoneAccessRules;

public class GetZoneAccessRulesQuery : IRequest<IReadOnlyList<ZoneAccessRuleDto>>
{
    public int ZoneId { get; set; }
    public int RequestedByUserId { get; set; }
}
