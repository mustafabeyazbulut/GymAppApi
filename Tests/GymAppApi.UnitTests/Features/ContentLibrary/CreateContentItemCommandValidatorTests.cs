using System.Text;
using GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;
using GymAppApi.Domain.Enums;

namespace GymAppApi.UnitTests.Features.ContentLibrary;

public class CreateContentItemCommandValidatorTests
{
    private readonly CreateContentItemCommandValidator _validator = new();

    private static CreateContentItemCommand ValidCommand() => new()
    {
        Title = "Squat Tekniği",
        RequiredAccessTier = PackageAccessTier.Standard,
        FileContent = new MemoryStream(Encoding.UTF8.GetBytes("x")),
        FileContentType = "video/mp4",
        RequestedByUserId = 1,
    };

    [Fact]
    public void Validate_WhenCommandIsValid_HasNoErrors()
    {
        var result = _validator.Validate(ValidCommand());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenTitleIsEmpty_HasError()
    {
        var command = ValidCommand();
        command.Title = "";
        var result = _validator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenFileContentTypeIsEmpty_HasError()
    {
        var command = ValidCommand();
        command.FileContentType = "";
        var result = _validator.Validate(command);
        Assert.False(result.IsValid);
    }
}
