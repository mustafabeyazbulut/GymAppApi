using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Localization;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.CreateClassSession;

public class CreateClassSessionCommandValidator : AbstractValidator<CreateClassSessionCommand>
{
    public CreateClassSessionCommandValidator()
    {
        RuleFor(x => x.BranchId).GreaterThan(0);
        RuleFor(x => x.TrainerUserId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x.CancellationCutoffHours).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
            .WithMessage(_ => Localized("ClassSessionEndTimeMustBeAfterStartTime"));
    }

    private static string Localized(string code) =>
        AppMessages.Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}
