using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class InvalidPackageAssignmentInvitationCodeException : UnauthorizedException
{
    public InvalidPackageAssignmentInvitationCodeException() : base("Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı.") { }
}
