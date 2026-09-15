using GymAppApi.Domain.Entities;

namespace GymAppApi.UnitTests.Domain;

public class RefreshTokenTests
{
    [Fact]
    public void NewRefreshToken_DefaultsToNotRevoked()
    {
        var token = new RefreshToken
        {
            UserId = 1,
            TokenHash = "hash",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        Assert.Null(token.RevokedAt);
        Assert.Null(token.ReplacedByTokenHash);
    }
}
