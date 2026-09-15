using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public class UnauthorizedException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Unauthorized;

    public UnauthorizedException(string message) : base(message) { }
}
