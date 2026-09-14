using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public abstract class BaseException : Exception
{
    public abstract HttpStatusCode StatusCode { get; }

    protected BaseException(string message) : base(message) { }
}
