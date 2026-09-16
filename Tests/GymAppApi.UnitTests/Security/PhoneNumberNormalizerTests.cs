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
}
