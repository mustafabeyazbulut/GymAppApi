using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GymAppApi.WebApi.Authorization;

// Aktif bağlamın firması pasif (kapatılmış) iken personel yazma uçları 403
// CompanyInactive döner. Aksi hâlde global filtre pasif firmayı gizlediği
// için bu istekler yanıltıcı bir 404 "Firma bulunamadı" ile düşüyordu.
//
// Sadece personel politikasıyla korunan uçlar kapsanır: aynı kullanıcı başka
// bir gym'in üyesi de olabilir ve kişisel uçları (dil, profil, kişisel takip,
// davet kabulü vb.) çalışmaya devam etmelidir. Okumalar etkilenmez.
public class ActiveCompanyRequiredFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> StaffPolicies = new(StringComparer.Ordinal)
    {
        "StaffManagement", "GymAdminOnly", "GymAdminOrSuperAdmin", "ContentManagement",
    };

    private readonly ITenantContext _tenantContext;

    public ActiveCompanyRequiredFilter(ITenantContext tenantContext) => _tenantContext = tenantContext;

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_tenantContext.CompanyInactive && IsWrite(context.HttpContext.Request.Method) && IsStaffEndpoint(context))
        {
            throw new ForbiddenException("CompanyInactive");
        }

        return next();
    }

    private static bool IsWrite(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    private static bool IsStaffEndpoint(ActionExecutingContext context) =>
        context.ActionDescriptor.EndpointMetadata.OfType<AuthorizeAttribute>()
            .Any(a => a.Policy is not null && StaffPolicies.Contains(a.Policy));
}
