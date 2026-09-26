using GymAppApi.Infrastructure.Security;

namespace GymAppApi.UnitTests.Security;

public class PhoneNumberNormalizerTests
{
    private readonly PhoneNumberNormalizer _normalizer = new();

    [Theory]
    [InlineData("05551234567", "+905551234567")]
    [InlineData("5551234567", "+905551234567")]
    [InlineData("+905551234567", "+905551234567")]
    [InlineData("0555 123 45 67", "+905551234567")]
    public void NormalizeIfPhone_TurkishMobileNumberInAnyCommonFormat_ReturnsE164(string input, string expected)
    {
        Assert.Equal(expected, _normalizer.NormalizeIfPhone(input));
    }

    [Fact]
    public void NormalizeIfPhone_EmailAddress_ReturnsUnchanged()
    {
        Assert.Equal("ayse@test.com", _normalizer.NormalizeIfPhone("ayse@test.com"));
    }

    [Fact]
    public void NormalizeIfPhone_UnparseableGarbage_ReturnsUnchanged()
    {
        Assert.Equal("not-a-phone-number", _normalizer.NormalizeIfPhone("not-a-phone-number"));
    }

    [Fact]
    public void NormalizeIfPhone_EmptyString_ReturnsUnchanged()
    {
        Assert.Equal("", _normalizer.NormalizeIfPhone(""));
    }

    [Fact]
    public void NormalizeIfPhone_ForeignE164Number_ReturnsItUnchanged()
    {
        Assert.Equal("+4915123456789", _normalizer.NormalizeIfPhone("+49 151 23456789"));
    }

    // --- TryNormalize: katı doğrulama + kanonik E.164 ---

    [Theory]
    // Türkiye - yerel biçimler geriye dönük uyumluluk için +90'a normalize edilir.
    [InlineData("05551234567", "+905551234567")]
    [InlineData("5551234567", "+905551234567")]
    [InlineData("+905551234567", "+905551234567")]
    // Almanya, ABD, Birleşik Krallık - E.164.
    [InlineData("+4915123456789", "+4915123456789")]
    [InlineData("+12015550123", "+12015550123")]
    [InlineData("+447400123456", "+447400123456")]
    // Boşluk / tire / parantez / nokta temizlenir.
    [InlineData("+90 555 123 45 67", "+905551234567")]
    [InlineData("+90-555-123-45-67", "+905551234567")]
    [InlineData("+1 (201) 555-0123", "+12015550123")]
    [InlineData("+44 7400.123.456", "+447400123456")]
    [InlineData("  +4915123456789  ", "+4915123456789")]
    // Uluslararası "00" öneki.
    [InlineData("0049 151 23456789", "+4915123456789")]
    public void TryNormalize_ValidNumber_ReturnsTrueAndCanonicalE164(string input, string expected)
    {
        Assert.True(_normalizer.TryNormalize(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    // Geçersiz uzunluk.
    [InlineData("+90555123")]
    [InlineData("+9055512345678901")]
    [InlineData("+49151")]
    // Harf içeren giriş (libphonenumber harfleri tuş takımı rakamlarına
    // çevirebildiği için ayrıca reddedilmeli).
    [InlineData("+90555ABC4567")]
    [InlineData("0555-FLOWERS")]
    [InlineData("ayse@test.com")]
    // Ülke kodu geçerli ama numara o ülke için geçersiz.
    [InlineData("+900001234567")]
    public void TryNormalize_InvalidInput_ReturnsFalse(string? input)
    {
        Assert.False(_normalizer.TryNormalize(input, out _));
    }
}
