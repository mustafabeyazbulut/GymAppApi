using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Localization;

namespace GymAppApi.Application.Common.Validation;

public static class PhoneNumberValidationExtensions
{
    // Telefon alan tüm validator'ların tek ortak kuralı - handler'ların
    // kullandığı IPhoneNumberNormalizer.TryNormalize ile birebir aynı kabul
    // kümesi, böylece validator'dan geçen her değer handler'da kanonik
    // E.164'e çevrilebilir (eski "^\+[1-9]\d{7,14}$" regex'i yerine).
    public static IRuleBuilderOptions<T, string> ValidPhoneNumber<T>(this IRuleBuilder<T, string> ruleBuilder, IPhoneNumberNormalizer normalizer) =>
        ruleBuilder
            .Must(phone => normalizer.TryNormalize(phone, out _))
            .WithMessage(_ => AppMessages.Resolve("InvalidPhoneNumber", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
}
