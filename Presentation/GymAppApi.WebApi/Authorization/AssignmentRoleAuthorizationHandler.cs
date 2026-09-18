using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.WebApi.Authorization;

// Her istekte Assignment'ı bir JWT claim'ine güvenmek yerine bilinçli olarak
// yeniden sorguluyor — bkz. backend spec'inin "Auth/Yetkilendirme Altyapısı"
// bölümü: bir kullanıcının Assignment'ları (rol verilme/iptal) token
// çıkarıldıktan SONRA değişebilir, ve access token'ın kısa ömrü bu kadar
// yıkıcı/idari bir işlem için bayat bir claim'i kabul edilebilir kılacak
// kadar kısa değil.
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

        // AnyAsync yerine IgnoreQueryFilters + GetAllAsync: bu kontrol
        // çalıştığında çağıranın ambient tenant context'i henüz gerçek
        // anlamda kullanışlı değil - TenantResolutionService, CompanyId'yi
        // çağıranın İLK (Id'ye göre) Assignment'ından çözüyor, bu yüzden ilk
        // oluşturulan Assignment'ı bu requirement'ın gerçekten önemsediği
        // şirketten FARKLI bir şirkette olan çok-şirketli bir personel,
        // gerçek GymAdmin/BranchManager/SuperAdmin satırının Assignment'ın
        // kendi CompanyId sorgu filtresi tarafından sessizce gizlenmesiyle
        // karşılaşırdı - o ikinci şirket için policy ile korunan HER işlemi
        // yanlışlıkla reddeder. AnyAsync'in hiçbir IgnoreQueryFilters kaçış
        // yolu yok (bkz. .claude/memory/project-member-package-linkage-
        // design.md'deki standing rule), bu yüzden bunun yerine GetAllAsync+
        // Count kullanılmalı - bu kod tabanındaki Member/çok-şirketli
        // erişilebilir her kontrolde zaten kullanılan aynı düzeltme deseni.
        var assignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == userId && a.IsActive && requirement.AllowedRoles.Contains(a.Role),
            include: q => q.IgnoreQueryFilters().Include(a => a.User),
            cancellationToken: CancellationToken.None);

        if (assignments.Count > 0)
        {
            context.Succeed(requirement);
        }
    }
}
