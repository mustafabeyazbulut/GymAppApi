using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;

public class UpdatePreferredLanguageCommandValidator : AbstractValidator<UpdatePreferredLanguageCommand>
{
    // Keep in sync with GymApp mobile's AppLocalizations.supportedLocales (tr, en).
    private static readonly string[] SupportedLanguages = { "tr", "en" };

    public UpdatePreferredLanguageCommandValidator()
    {
        RuleFor(x => x.Language).NotEmpty().Must(l => SupportedLanguages.Contains(l))
            .WithMessage($"Language must be one of: {string.Join(", ", SupportedLanguages)}.");
    }
}
