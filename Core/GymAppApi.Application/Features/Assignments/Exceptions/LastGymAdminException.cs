using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class LastGymAdminException : ConflictException
{
    public LastGymAdminException() : base("LastGymAdmin") { }
}
