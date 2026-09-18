using FluentValidation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommandValidator : AbstractValidator<CreatePackageCommand>
{
    public CreatePackageCommandValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);

        RuleFor(x => x.DurationDays).NotNull().When(x => x.Type == PackageType.Duration)
            .WithMessage("DurationDays is required for a Duration package.");
        RuleFor(x => x.SessionCount).Null().When(x => x.Type == PackageType.Duration)
            .WithMessage("SessionCount must not be set for a Duration package.");

        RuleFor(x => x.SessionCount).NotNull().GreaterThan(0).When(x => x.Type == PackageType.SessionBased)
            .WithMessage("SessionCount is required and must be greater than 0 for a SessionBased package.");
    }
}
