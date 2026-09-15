using System.Net;
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.UnitTests.Common;

public class ExceptionStatusCodeTests
{
    [Fact]
    public void UnauthorizedException_HasStatusCode401()
    {
        var exception = new UnauthorizedException("Kimlik veya şifre hatalı.");
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public void ForbiddenException_HasStatusCode403()
    {
        var exception = new ForbiddenException("Bu işlem için yetkiniz yok.");
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }
}
