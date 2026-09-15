using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidRefreshTokenException : UnauthorizedException
{
    public InvalidRefreshTokenException() : base("Oturum süresi doldu, lütfen tekrar giriş yapın.") { }
}
