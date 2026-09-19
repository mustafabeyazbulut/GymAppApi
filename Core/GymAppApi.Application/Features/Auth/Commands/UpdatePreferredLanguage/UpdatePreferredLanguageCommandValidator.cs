using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Localization;

namespace GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;

public class UpdatePreferredLanguageCommandValidator : AbstractValidator<UpdatePreferredLanguageCommand>
{
    // GymApp mobil tarafının AppLocalizations.supportedLocales'i (tr, en) ile senkron tut.
    private static readonly string[] SupportedLanguages = { "tr", "en" };

    public UpdatePreferredLanguageCommandValidator()
    {
        RuleFor(x => x.Language).NotEmpty().Must(l => SupportedLanguages.Contains(l))
            // WithMessage burada Func<T,string> alıyor - istisnaların aksine
            // FluentValidation mesajı doğrulama ANINDA (isteğin kendi
            // CurrentUICulture'ı zaten Program.cs'teki
            // UseRequestLocalization tarafından ayarlanmışken) hesaplıyor,
            // bu yüzden burada hardcode bir dil olmadan doğrudan
            // AppMessages'a bakabiliyoruz.
            .WithMessage(_ => AppMessages.Resolve(
                "UnsupportedLanguage",
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                string.Join(", ", SupportedLanguages)));
    }
}
