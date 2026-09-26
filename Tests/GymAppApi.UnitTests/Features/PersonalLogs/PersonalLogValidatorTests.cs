using System.Globalization;
using GymAppApi.Application.Features.PersonalLogs.Commands.CreatePersonalLog;
using GymAppApi.Application.Features.PersonalLogs.Commands.UpdatePersonalLog;
using GymAppApi.Domain.Enums;

namespace GymAppApi.UnitTests.Features.PersonalLogs;

public class PersonalLogValidatorTests
{
    private readonly CreatePersonalLogCommandValidator _validator = new();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static CreatePersonalLogCommand Workout(string? title = "Bacak günü", int? duration = 45) => new()
    {
        Date = Today,
        Kind = PersonalLogKind.Workout,
        Title = title,
        DurationMinutes = duration,
    };

    private static CreatePersonalLogCommand Measurement(decimal? weight = null, decimal? bodyFat = null, decimal? waist = null) => new()
    {
        Date = Today,
        Kind = PersonalLogKind.Measurement,
        WeightKg = weight,
        BodyFatPercent = bodyFat,
        WaistCm = waist,
    };

    [Fact]
    public void Workout_WithTitleAndDuration_IsValid() => Assert.True(_validator.Validate(Workout()).IsValid);

    [Fact]
    public void Workout_WithoutDuration_IsValid() => Assert.True(_validator.Validate(Workout(duration: null)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Workout_WithoutTitle_IsInvalid(string? title) => Assert.False(_validator.Validate(Workout(title: title)).IsValid);

    [Fact]
    public void Workout_WithTitleOver100Characters_IsInvalid() =>
        Assert.False(_validator.Validate(Workout(title: new string('x', 101))).IsValid);

    [Theory]
    [InlineData(1, true)]
    [InlineData(600, true)]
    [InlineData(0, false)]
    [InlineData(601, false)]
    public void Workout_DurationBounds(int duration, bool expected) =>
        Assert.Equal(expected, _validator.Validate(Workout(duration: duration)).IsValid);

    [Fact]
    public void Measurement_WithoutAnyValue_IsInvalid() => Assert.False(_validator.Validate(Measurement()).IsValid);

    [Theory]
    [InlineData(20, true)]
    [InlineData(400, true)]
    [InlineData(19.9, false)]
    [InlineData(400.1, false)]
    public void Measurement_WeightBounds(double weight, bool expected) =>
        Assert.Equal(expected, _validator.Validate(Measurement(weight: (decimal)weight)).IsValid);

    [Theory]
    [InlineData(1, true)]
    [InlineData(75, true)]
    [InlineData(0.9, false)]
    [InlineData(75.1, false)]
    public void Measurement_BodyFatBounds(double bodyFat, bool expected) =>
        Assert.Equal(expected, _validator.Validate(Measurement(bodyFat: (decimal)bodyFat)).IsValid);

    [Theory]
    [InlineData(30, true)]
    [InlineData(250, true)]
    [InlineData(29.9, false)]
    [InlineData(250.1, false)]
    public void Measurement_WaistBounds(double waist, bool expected) =>
        Assert.Equal(expected, _validator.Validate(Measurement(waist: (decimal)waist)).IsValid);

    [Fact]
    public void Measurement_DoesNotRequireATitle() => Assert.True(_validator.Validate(Measurement(weight: 80)).IsValid);

    [Fact]
    public void NotesOver1000Characters_IsInvalid()
    {
        var command = Workout();
        command.Notes = new string('x', 1001);
        Assert.False(_validator.Validate(command).IsValid);
    }

    [Fact]
    public void AFutureDate_IsInvalid()
    {
        var command = Workout();
        command.Date = Today.AddDays(2);
        Assert.False(_validator.Validate(command).IsValid);
    }

    [Fact]
    public void AnUndefinedKind_IsInvalid()
    {
        var command = Workout();
        command.Kind = (PersonalLogKind)99;
        Assert.False(_validator.Validate(command).IsValid);
    }

    [Fact]
    public void Messages_ComeFromTheLocalizedCatalog()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr");
            var errors = _validator.Validate(Measurement()).Errors;
            Assert.Contains(errors, e => e.ErrorMessage == "En az bir ölçüm değeri (kilo, yağ oranı veya bel çevresi) girilmelidir.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void UpdateCommand_UsesTheSameRules()
    {
        var validator = new UpdatePersonalLogCommandValidator();
        Assert.False(validator.Validate(new UpdatePersonalLogCommand { Date = Today, Kind = PersonalLogKind.Workout }).IsValid);
        Assert.True(validator.Validate(new UpdatePersonalLogCommand { Date = Today, Kind = PersonalLogKind.Measurement, WaistCm = 80 }).IsValid);
    }
}
