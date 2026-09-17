using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class LastGymAdminException : ConflictException
{
    public LastGymAdminException() : base(
        "Bir firmanın en az bir Gym Admin'i olmalı. Son Gym Admin'i kaldırmak için Super Admin gerekir.") { }
}
