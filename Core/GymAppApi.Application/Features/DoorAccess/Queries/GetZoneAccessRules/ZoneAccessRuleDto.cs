namespace GymAppApi.Application.Features.DoorAccess.Queries.GetZoneAccessRules;

public class ZoneAccessRuleDto
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public string RuleType { get; set; } = null!;
    public string? RuleValue { get; set; }
}
