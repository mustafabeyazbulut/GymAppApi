using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidResetCodeException : UnauthorizedException
{
    public InvalidResetCodeException() : base("InvalidResetCode") { }
}
