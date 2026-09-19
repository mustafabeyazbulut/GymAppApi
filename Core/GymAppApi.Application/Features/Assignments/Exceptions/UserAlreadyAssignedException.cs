using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class UserAlreadyAssignedException : ConflictException
{
    public UserAlreadyAssignedException() : base("UserAlreadyAssigned") { }
}
