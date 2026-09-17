using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class InvalidAssignmentInvitationCodeException : UnauthorizedException
{
    public InvalidAssignmentInvitationCodeException() : base("Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı.") { }
}
