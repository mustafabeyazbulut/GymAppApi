using GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

namespace GymAppApi.UnitTests.Features.Packages;

public class RecordProgressNoteCommandValidatorTests
{
    private readonly RecordProgressNoteCommandValidator _validator = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void Validate_WithScoresInRange_IsValid(int technique, int condition)
    {
        var result = _validator.Validate(new RecordProgressNoteCommand { TechniqueScore = technique, ConditionScore = condition });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(101, 50)]
    [InlineData(50, -1)]
    [InlineData(50, 101)]
    public void Validate_WithScoreOutOfRange_IsInvalid(int technique, int condition)
    {
        var result = _validator.Validate(new RecordProgressNoteCommand { TechniqueScore = technique, ConditionScore = condition });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithNoteTextOver1000Characters_IsInvalid()
    {
        var result = _validator.Validate(new RecordProgressNoteCommand
        {
            TechniqueScore = 50,
            ConditionScore = 50,
            NoteText = new string('x', 1001),
        });

        Assert.False(result.IsValid);
    }
}
