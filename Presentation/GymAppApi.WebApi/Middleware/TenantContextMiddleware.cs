using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Tenancy;

namespace GymAppApi.WebApi.Middleware;

// Runs once per request, right after authentication and before
// authorization/endpoint execution, so every downstream query filter and
// every AssignmentRoleAuthorizationHandler check sees the caller's real
// tenant scope. This can't just reuse IUnitOfWork/IReadRepository<Assignment>
// to look itself up: Assignment is itself tenant-filtered, so querying "what
// are this user's own assignments" through the normal filtered path, before
// the tenant context is known, is circular. ITenantResolutionService's
// implementation sidesteps this with one explicit .IgnoreQueryFilters() call.
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AmbientTenantContext tenantContext, ITenantResolutionService resolutionService)
    {
        var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (subClaim is not null && int.TryParse(subClaim, out var userId))
        {
            var resolved = await resolutionService.ResolveForUserAsync(userId, context.RequestAborted);
            tenantContext.IsSuperAdmin = resolved.IsSuperAdmin;
            tenantContext.CompanyId = resolved.CompanyId;
            tenantContext.BranchId = resolved.BranchId;
        }

        await _next(context);
    }
}
