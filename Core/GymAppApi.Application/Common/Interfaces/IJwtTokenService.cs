namespace GymAppApi.Application.Common.Interfaces;

public record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public record AccessTokenClaims(int UserId, string FullName, string? Email, string Phone);

public interface IJwtTokenService
{
    AccessTokenResult GenerateAccessToken(AccessTokenClaims claims);

    // Returns the RAW refresh token (never persisted as-is — callers hash it
    // via IPasswordHasher before storing in RefreshToken.TokenHash).
    string GenerateRefreshTokenValue();
}
