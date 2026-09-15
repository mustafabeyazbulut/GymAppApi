using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidCredentialsException : UnauthorizedException
{
    public InvalidCredentialsException() : base("Telefon numarası/e-posta veya şifre hatalı.") { }
}
