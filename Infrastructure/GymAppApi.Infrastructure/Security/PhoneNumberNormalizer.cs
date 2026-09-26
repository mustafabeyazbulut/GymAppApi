using System.Text.RegularExpressions;
using GymAppApi.Application.Common.Interfaces;
using PhoneNumbers;

namespace GymAppApi.Infrastructure.Security;

// Telefonlar libphonenumber-csharp (Google libphonenumber'ın portu) ile
// doğrulanıyor: ülke bazlı gerçek numara planlarını (uzunluk, geçerli
// aralıklar, mobil/sabit) bilen tek yaygın .NET kütüphanesi. Bir regex
// "+[1-9]\d{7,14}" sadece biçimi kontrol eder, "+900001234567" gibi o ülkede
// var olamayacak numaraları kabul ederdi.
//
// "+" ile gelen giriş (E.164) her ülke için ülke kodundan çözülür. "+"
// olmadan gelen yerel biçimler ("05551234567", "5551234567") geriye dönük
// uyumluluk için varsayılan bölge Türkiye olarak yorumlanır - mobil artık
// her zaman E.164 gönderiyor, bu yol eski istemciler ve login/forgot-password
// ekranlarında elle yazılan TR numaraları için duruyor.
public class PhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private const string DefaultRegion = "TR";
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    // Temizlenen biçimlendirme karakterleri: boşluk, tire, parantez, nokta, eğik çizgi.
    private static readonly Regex FormattingCharacters = new(@"[\s\-().\/]", RegexOptions.Compiled);

    // Temizlendikten sonra kalan giriş sadece rakam (ve baştaki opsiyonel "+")
    // olmalı - libphonenumber harfleri tuş takımı rakamlarına çevirebildiği
    // için ("0555-FLOWERS") harfler burada ayrıca reddediliyor.
    private static readonly Regex DigitsOnly = new(@"^\+?\d+$", RegexOptions.Compiled);

    public string NormalizeIfPhone(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Contains('@'))
        {
            return identifier;
        }

        return TryNormalize(identifier, out var e164) ? e164 : identifier;
    }

    public bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cleaned = FormattingCharacters.Replace(input, string.Empty);
        if (!DigitsOnly.IsMatch(cleaned))
        {
            return false;
        }

        try
        {
            var parsed = Util.Parse(cleaned, DefaultRegion);
            if (!Util.IsValidNumber(parsed))
            {
                return false;
            }

            e164 = Util.Format(parsed, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }
}
