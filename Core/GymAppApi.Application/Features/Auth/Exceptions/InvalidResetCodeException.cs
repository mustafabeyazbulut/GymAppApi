using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidResetCodeException : UnauthorizedException
{
    public InvalidResetCodeException() : base("Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı.") { }
}
