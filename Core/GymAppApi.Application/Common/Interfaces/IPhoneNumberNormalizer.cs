namespace GymAppApi.Application.Common.Interfaces;

public interface IPhoneNumberNormalizer
{
    // If `identifier` parses as a valid phone number for the default region
    // (see the Infrastructure implementation), returns its canonical E.164
    // form (e.g. "05551234567" or "5551234567" -> "+905551234567"). Anything
    // that doesn't parse as a phone number (an email address, already-E.164
    // input, unparseable garbage) is returned unchanged.
    string NormalizeIfPhone(string identifier);
}
