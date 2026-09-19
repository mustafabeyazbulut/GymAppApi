using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.ClassScheduling.Exceptions;

public class AlreadyEnrolledInClassSessionException : ConflictException
{
    public AlreadyEnrolledInClassSessionException() : base("AlreadyEnrolledInClassSession") { }
}
