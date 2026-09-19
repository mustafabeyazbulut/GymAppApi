using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class InvalidAssignmentInvitationCodeException : UnauthorizedException
{
    public InvalidAssignmentInvitationCodeException() : base("InvalidAssignmentInvitationCode") { }
}
