using GymAppApi.Application.Common.RateLimiting;
using GymAppApi.Application.Features.Auth.Common;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Login;

public class LoginCommand : IRequest<AuthTokenResult>, IRateLimitedByIdentifier
{
    public string Identifier { get; set; } = null!; // phone or email
    public string Password { get; set; } = null!;

    // IP değiştirerek tek bir hesaba yönelik şifre denemesine karşı: IP bazlı
    // sınıra ek olarak tanımlayıcı bazlı sınır (IdentifierRateLimitFilter).
    string? IRateLimitedByIdentifier.RateLimitIdentifier => Identifier;
}
