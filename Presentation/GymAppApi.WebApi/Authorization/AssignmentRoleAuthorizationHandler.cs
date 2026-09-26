using GymAppApi.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace GymAppApi.WebApi.Authorization;

// Politika kararı isteğin AKTİF atamasının rolüne göre verilir - "kullanıcının
// herhangi bir yerde bu rolde bir ataması var mı" diye bakılmaz. Aksi hâlde
// A firmasında Trainer olarak hareket eden (X-Active-Assignment-Id) biri, B
// firmasındaki GymAdmin ataması yüzünden A'da GymAdmin'e özel uç noktalara
// geçebilirdi.
//
// Aktif atama bir JWT claim'i değil: TenantContextMiddleware her istekte
// çağıranın atamalarını DB'den yeniden okuyup (ITenantResolutionService)
// ambient ITenantContext'i kuruyor - rol verilip/iptal edildiğinde access
// token'ın ömrünü beklemeden hemen etkili olur (backend spec'inin
// "Auth/Yetkilendirme Altyapısı" bölümü). Bu handler o middleware'den
// SONRA (UseAuthorization) çalışır.
public class AssignmentRoleAuthorizationHandler : AuthorizationHandler<AssignmentRoleRequirement>
{
    private readonly ITenantContext _tenantContext;

    public AssignmentRoleAuthorizationHandler(ITenantContext tenantContext) => _tenantContext = tenantContext;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AssignmentRoleRequirement requirement)
    {
        if (_tenantContext.Role is { } activeRole && requirement.AllowedRoles.Contains(activeRole))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
