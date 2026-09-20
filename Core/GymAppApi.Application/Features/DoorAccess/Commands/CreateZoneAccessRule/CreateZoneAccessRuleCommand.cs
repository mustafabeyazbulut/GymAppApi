using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateZoneAccessRule;

public class CreateZoneAccessRuleCommand : IRequest<CreateZoneAccessRuleCommandResult>
{
    // Route segmentinden controller tarafından set edilir.
    public int ZoneId { get; set; }
    public ZoneAccessRuleType RuleType { get; set; }
    public string? RuleValue { get; set; }

    public int RequestedByUserId { get; set; }
}
