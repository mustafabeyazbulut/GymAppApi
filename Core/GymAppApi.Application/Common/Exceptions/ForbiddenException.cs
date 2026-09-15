using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public class ForbiddenException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Forbidden;

    public ForbiddenException(string message) : base(message) { }
}
