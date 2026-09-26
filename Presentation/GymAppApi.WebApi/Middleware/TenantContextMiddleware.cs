using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Common.Exceptions;
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

    // JWT claim değil header: çok-şirketli bir personelin şu an hangi şirket
    // olarak hareket ettiği bir access token'ın ömründen çok daha sık
    // değişir, ve bir claim'in aksine bu her zaman sadece bir İPUCU -
    // ITenantResolutionService yine de çağıranın orada gerçekten canlı bir
    // Assignment'ı olmasını şart koşuyor, bu yüzden sahte/bayat bir header
    // değeri çağıranın kendi Assignment'larının zaten izin verdiğinden fazla
    // erişim asla veremez.
    public const string ActiveCompanyHeaderName = "X-Active-Company-Id";

    // Mobilin her istekte gönderdiği, kullanıcının kendi personel
    // atamalarından birinin Id'si - bağlam (CompanyId, BranchId, Role)
    // tamamen o atamadan kurulur. Company header'ının aksine bu bir ipucu
    // DEĞİL: geçersizse (çağırana ait değil, pasif, personel ataması değil
    // veya sayı değil) istek 403 ile reddedilir.
    public const string ActiveAssignmentHeaderName = "X-Active-Assignment-Id";

    public async Task InvokeAsync(HttpContext context, AmbientTenantContext tenantContext, ITenantResolutionService resolutionService)
    {
        var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (subClaim is not null && int.TryParse(subClaim, out var userId))
        {
            int? preferredCompanyId = context.Request.Headers.TryGetValue(ActiveCompanyHeaderName, out var companyHeaderValue)
                && int.TryParse(companyHeaderValue, out var parsedCompanyId)
                    ? parsedCompanyId
                    : null;

            int? activeAssignmentId = null;
            if (context.Request.Headers.TryGetValue(ActiveAssignmentHeaderName, out var assignmentHeaderValue))
            {
                if (!int.TryParse(assignmentHeaderValue, out var parsedAssignmentId))
                {
                    throw new ForbiddenException("InvalidActiveAssignment");
                }

                activeAssignmentId = parsedAssignmentId;
            }

            var resolved = await resolutionService.ResolveForUserAsync(userId, preferredCompanyId, activeAssignmentId, context.RequestAborted);
            if (resolved.ActiveAssignmentRejected)
            {
                throw new ForbiddenException("InvalidActiveAssignment");
            }

            tenantContext.IsSuperAdmin = resolved.IsSuperAdmin;
            tenantContext.CompanyId = resolved.CompanyId;
            tenantContext.BranchId = resolved.BranchId;
            tenantContext.AssignmentId = resolved.AssignmentId;
            tenantContext.Role = resolved.Role;
            tenantContext.CompanyInactive = resolved.CompanyInactive;
        }

        await _next(context);
    }
}
