using GymAppApi.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace GymAppApi.WebApi.Authorization;

public class AssignmentRoleRequirement : IAuthorizationRequirement
{
    public AssignmentRoleRequirement(params AssignmentRole[] allowedRoles) => AllowedRoles = allowedRoles;

    public AssignmentRole[] AllowedRoles { get; }
}
