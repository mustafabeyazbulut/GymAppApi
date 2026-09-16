using GymAppApi.Application.Common.Interfaces;
using PhoneNumbers;

namespace GymAppApi.Infrastructure.Security;

// Lets a user type a phone-based login/forgot-password identifier in any
// common local format ("05551234567", "5551234567", "+905551234567", with
// spaces/dashes) without needing a country-code picker on those screens —
// only Register's form (a different, new-account context) asks for a
// country explicitly via IntlPhoneField. Defaults to Turkey since that's
// this app's only market today; a genuinely multi-country deployment would
// need a smarter default-region strategy (e.g. from Accept-Language or a
// user preference) instead of a hardcoded "TR".
public class PhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private const string DefaultRegion = "TR";
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public string NormalizeIfPhone(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Contains('@'))
        {
            return identifier;
        }

        try
        {
            var parsed = Util.Parse(identifier, DefaultRegion);
            return Util.IsValidNumber(parsed) ? Util.Format(parsed, PhoneNumberFormat.E164) : identifier;
        }
        catch (NumberParseException)
        {
            return identifier;
        }
    }
}
