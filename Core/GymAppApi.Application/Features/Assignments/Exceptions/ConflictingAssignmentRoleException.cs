using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class ConflictingAssignmentRoleException : ConflictException
{
    public ConflictingAssignmentRoleException() : base("ConflictingAssignmentRole") { }
}
