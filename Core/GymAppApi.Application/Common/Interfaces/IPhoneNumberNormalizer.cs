namespace GymAppApi.Application.Common.Interfaces;

public interface IPhoneNumberNormalizer
{
    // If `identifier` parses as a valid phone number for the default region
    // (see the Infrastructure implementation), returns its canonical E.164
    // form (e.g. "05551234567" or "5551234567" -> "+905551234567"). Anything
    // that doesn't parse as a phone number (an email address, already-E.164
    // input, unparseable garbage) is returned unchanged.
    string NormalizeIfPhone(string identifier);

    // Katı sürüm: giriş, herhangi bir ülke için GERÇEKTEN geçerli bir telefon
    // numarasıysa true döner ve kanonik E.164 biçimini verir. E.164 ("+49...")
    // her ülke için kabul edilir; "+" olmadan gelen yerel biçimler geriye
    // dönük uyumluluk için Türkiye (+90) olarak yorumlanır. Boşluk, tire,
    // parantez ve nokta temizlenir; harf içeren giriş reddedilir. Telefon alan
    // tüm komutların validator'ları ve handler'ları bunu kullanır - DB'de
    // telefon her zaman bu kanonik biçimde saklanır ve aranır.
    bool TryNormalize(string? input, out string e164);
}
