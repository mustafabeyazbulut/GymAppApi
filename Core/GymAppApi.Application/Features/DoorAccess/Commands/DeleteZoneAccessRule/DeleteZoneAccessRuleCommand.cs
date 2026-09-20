using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.DeleteZoneAccessRule;

public class DeleteZoneAccessRuleCommand : IRequest
{
    public int ZoneAccessRuleId { get; set; }
    public int RequestedByUserId { get; set; }
}
