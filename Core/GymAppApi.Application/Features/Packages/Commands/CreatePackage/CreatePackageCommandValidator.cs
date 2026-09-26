using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Localization;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommandValidator : AbstractValidator<CreatePackageCommand>
{
    public CreatePackageCommandValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        // Senaryo §10.5: firma geneli (şubesiz) paket yok - her paket bir şubeye ait.
        RuleFor(x => x.BranchId).NotNull().GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);

        // NotNull tek başına 0 veya negatif bir değeri kabul ederdi - bir
        // GymAdmin'in "0 gün süreli" gibi anlamsız bir paket tanımlamasını
        // engeller.
        RuleFor(x => x.DurationDays).NotNull().GreaterThan(0).When(x => x.Type == PackageType.Duration)
            .WithMessage(_ => Localized("DurationDaysRequiredForDurationPackage"));
        RuleFor(x => x.SessionCount).Null().When(x => x.Type == PackageType.Duration)
            .WithMessage(_ => Localized("SessionCountMustBeNullForDurationPackage"));

        RuleFor(x => x.SessionCount).NotNull().GreaterThan(0).When(x => x.Type == PackageType.SessionBased)
            .WithMessage(_ => Localized("SessionCountRequiredForSessionBasedPackage"));

        // null = sınırsız dondurma, 0 = paket dondurulamaz, N = toplam en fazla N gün.
        RuleFor(x => x.MaxFreezeDays).GreaterThanOrEqualTo(0).When(x => x.MaxFreezeDays != null)
            .WithMessage(_ => Localized("MaxFreezeDaysMustNotBeNegative"));
    }

    private static string Localized(string code) =>
        AppMessages.Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}
