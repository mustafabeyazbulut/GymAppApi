using GymAppApi.Application.Features.Packages.Commands.CreatePackage;
using GymAppApi.Domain.Enums;

namespace GymAppApi.UnitTests.Features.Packages;

public class CreatePackageCommandValidatorTests
{
    private readonly CreatePackageCommandValidator _validator = new();

    private static CreatePackageCommand ValidDurationCommand() => new()
    {
        CompanyId = 1,
        BranchId = 10,
        Name = "Aylık Üyelik",
        Type = PackageType.Duration,
        DurationDays = 30,
        Price = 1000m,
        ServiceIds = new List<int> { 5 },
    };

    [Fact]
    public void Validate_WithoutServices_HasError()
    {
        var command = ValidDurationCommand();
        command.ServiceIds = new List<int>();

        Assert.False(_validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Validate_WhenBranchIdIsMissing_HasError(int? branchId)
    {
        // Senaryo §10.5: firma geneli (şubesiz) paket yok.
        var command = ValidDurationCommand();
        command.BranchId = branchId;

        var result = _validator.Validate(command);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePackageCommand.BranchId));
    }

    [Fact]
    public void Validate_WhenDurationDaysIsZero_HasError()
    {
        var command = ValidDurationCommand();
        command.DurationDays = 0;

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenDurationDaysIsNegative_HasError()
    {
        var command = ValidDurationCommand();
        command.DurationDays = -5;

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    // Canlı test bulgusu: 0 = "dondurulamaz paket" (null = sınırsız) - geçerli bir değer.
    [Fact]
    public void Validate_WhenMaxFreezeDaysIsZero_HasNoError()
    {
        var command = ValidDurationCommand();
        command.MaxFreezeDays = 0;

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenMaxFreezeDaysIsNegative_HasError()
    {
        var command = ValidDurationCommand();
        command.MaxFreezeDays = -1;

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WhenMaxFreezeDaysIsNull_HasNoError()
    {
        var command = ValidDurationCommand();
        command.MaxFreezeDays = null;

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenMaxFreezeDaysIsPositive_HasNoError()
    {
        var command = ValidDurationCommand();
        command.MaxFreezeDays = 30;

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
