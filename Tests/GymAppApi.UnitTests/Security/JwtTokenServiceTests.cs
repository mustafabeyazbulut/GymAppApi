using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace GymAppApi.UnitTests.Security;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService() => new(Options.Create(new JwtOptions
    {
        SigningKey = "this-is-a-test-signing-key-at-least-32-bytes-long",
        Issuer = "GymAppApi.Tests",
        Audience = "GymApp.Tests",
        AccessTokenMinutes = 60
    }));

    [Fact]
    public void GenerateAccessToken_ProducesNonEmptyToken_ExpiringInTheFuture()
    {
        var service = CreateService();

        var result = service.GenerateAccessToken(new AccessTokenClaims(UserId: 1, FullName: "Test Kullanıcı", Email: "t@test.com", Phone: "+905551112233"));

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public void GenerateRefreshTokenValue_ProducesDifferentValuesEachCall()
    {
        var service = CreateService();

        var first = service.GenerateRefreshTokenValue();
        var second = service.GenerateRefreshTokenValue();

        Assert.NotEqual(first, second);
        Assert.True(first.Length > 20);
    }
}
