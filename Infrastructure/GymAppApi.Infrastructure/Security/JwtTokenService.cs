using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GymAppApi.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GymAppApi.Infrastructure.Security;

// No role/tenant/company claims by design (see the backend spec's "Auth/
// Yetkilendirme Altyapısı" section) — a user's Assignments can change after
// a token is issued, so role-gated endpoints re-query Assignment per request
// instead of trusting a claim that could go stale.
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public AccessTokenResult GenerateAccessToken(AccessTokenClaims claims)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var jwtClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.UserId.ToString()),
            new("name", claims.FullName),
            new("phone", claims.Phone),
        };
        if (!string.IsNullOrWhiteSpace(claims.Email))
        {
            jwtClaims.Add(new Claim("email", claims.Email));
        }

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: jwtClaims,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public string GenerateRefreshTokenValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
