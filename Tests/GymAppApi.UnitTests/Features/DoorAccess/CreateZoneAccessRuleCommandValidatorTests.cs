using GymAppApi.Application.Features.DoorAccess.Commands.CreateZoneAccessRule;
using GymAppApi.Domain.Enums;

namespace GymAppApi.UnitTests.Features.DoorAccess;

public class CreateZoneAccessRuleCommandValidatorTests
{
    private readonly CreateZoneAccessRuleCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenRuleTypeIsAllActiveMembersAndRuleValueIsNull_IsValid()
    {
        var result = _validator.Validate(new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.AllActiveMembers, RuleValue = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenRuleTypeIsAllActiveMembersAndRuleValueIsSet_IsInvalid()
    {
        var result = _validator.Validate(new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.AllActiveMembers, RuleValue = "x" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenRuleTypeIsGenderAndRuleValueIsNull_IsInvalid()
    {
        var result = _validator.Validate(new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.Gender, RuleValue = null });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenRuleTypeIsGenderAndRuleValueIsSet_IsValid()
    {
        var result = _validator.Validate(new CreateZoneAccessRuleCommand { ZoneId = 1, RuleType = ZoneAccessRuleType.Gender, RuleValue = "Kadın" });
        Assert.True(result.IsValid);
    }
}
