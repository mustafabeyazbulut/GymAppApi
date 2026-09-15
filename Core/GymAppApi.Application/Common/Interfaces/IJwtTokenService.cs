namespace GymAppApi.Application.Common.Interfaces;

public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    AccessTokenResult GenerateAccessToken(int userId, string fullName, string? email, string phone);

    // Returns the RAW refresh token (never persisted as-is — callers hash it
    // via IPasswordHasher before storing in RefreshToken.TokenHash).
    string GenerateRefreshTokenValue();
}
