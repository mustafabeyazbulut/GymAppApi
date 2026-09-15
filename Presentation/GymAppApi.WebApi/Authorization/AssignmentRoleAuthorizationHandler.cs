using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace GymAppApi.WebApi.Authorization;

// Deliberately re-queries Assignment on every request rather than trusting a
// JWT claim — see the backend spec's "Auth/Yetkilendirme Altyapısı" section:
// a user's Assignments can change (role granted/revoked) after a token is
// issued, and the access token's short lifetime isn't short enough to make a
// stale claim acceptable for a destructive/administrative action like this.
public class AssignmentRoleAuthorizationHandler : AuthorizationHandler<AssignmentRoleRequirement>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignmentRoleAuthorizationHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AssignmentRoleRequirement requirement)
    {
        var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (subClaim is null || !int.TryParse(subClaim, out var userId))
        {
            return;
        }

        var hasAllowedRole = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == userId && a.IsActive && requirement.AllowedRoles.Contains(a.Role),
            CancellationToken.None);

        if (hasAllowedRole)
        {
            context.Succeed(requirement);
        }
    }
}
