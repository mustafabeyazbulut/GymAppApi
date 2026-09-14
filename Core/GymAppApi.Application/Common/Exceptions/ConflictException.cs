using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public class ConflictException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Conflict;

    public ConflictException(string message) : base(message) { }
}
