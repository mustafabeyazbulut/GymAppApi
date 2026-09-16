using System.Net;
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class TooManyVerificationRequestsException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.TooManyRequests;

    public TooManyVerificationRequestsException() : base("Çok fazla kod isteği gönderildi. Lütfen daha sonra tekrar deneyin.") { }
}
