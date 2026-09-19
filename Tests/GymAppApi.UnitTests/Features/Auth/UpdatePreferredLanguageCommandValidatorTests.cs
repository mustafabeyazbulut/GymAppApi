using GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;

namespace GymAppApi.UnitTests.Features.Auth;

public class UpdatePreferredLanguageCommandValidatorTests
{
    private readonly UpdatePreferredLanguageCommandValidator _validator = new();

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void Validate_WhenLanguageIsSupported_HasNoErrors(string language)
    {
        var result = _validator.Validate(new UpdatePreferredLanguageCommand { UserId = 1, Language = language });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenLanguageIsUnsupported_HasError()
    {
        var result = _validator.Validate(new UpdatePreferredLanguageCommand { UserId = 1, Language = "fr" });

        Assert.False(result.IsValid);
    }
}
