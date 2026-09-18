using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

// AssignmentRoleAuthorizationHandler, her [Authorize(Policy =
// "GymAdminOrSuperAdmin"/"StaffManagement"/"SuperAdminOnly")] endpoint'inin
// arkasında duruyor - bu sınıf özellikle kaba policy kontrolünün,
// TenantResolutionService'in bu istek için "ambient" olarak hangi şirketi
// çözdüğünden bağımsız olarak doğru sonuç vermesi gereken çok-şirketli
// personel senaryosunu kapsıyor.
public class AssignmentRoleAuthorizationHandlerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AssignmentRoleAuthorizationHandlerTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055503{Random.Shared.Next(10000, 99999)}";

    [Fact]
    public async Task GymAdminOrSuperAdminPolicy_WithoutActiveCompanyHeader_StillFailsForACompanyThatIsNotTheCallersFirstAssignment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var companyA = new Company { Name = "Company A", IsActive = true };
        var companyB = new Company { Name = "Company B", IsActive = true };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var user = new User { FullName = "Multi Company Staff No Header", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = companyA.Id, Role = AssignmentRole.Trainer, IsActive = true });
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = companyB.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        // Bilinçli olarak X-Active-Company-Id set edilmiyor - ipucu olmadan
        // ambient resolution yine de Company A'da (ilk Assignment) kalır, bu
        // yüzden Company B, CreateBranchCommand'ın kendi Company aramasına
        // görünmez kalır. AssignmentRoleAuthorizationHandler düzeltmesi tek
        // başına (ambient olmayan bir şirkette var olan bir role artık kör
        // değil) gerekli ama header olmadan yeterli değil - bu test bunu
        // sabitliyor ki gelecekteki bir değişiklik sessizce "header hiçbir
        // şey yapmıyor" durumuna geri dönmesin.

        var response = await client.PostAsJsonAsync("/api/branches", new { companyId = companyB.Id, name = "Merkez Şube", address = "Adres 1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GymAdminOrSuperAdminPolicy_WithActiveCompanyHeader_SucceedsForACompanyThatIsNotTheCallersFirstAssignment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var companyA = new Company { Name = "Company A", IsActive = true };
        var companyB = new Company { Name = "Company B", IsActive = true };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var user = new User { FullName = "Multi Company Staff", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Ekleme sırası önemli: bu kullanıcının İLK Assignment'ı (en düşük
        // Id) Company A'da bir Trainer rolü - aşağıdaki ikinci isteğin
        // gerçekten ihtiyaç duyduğu GymAdmin rolü DEĞİL.
        // TenantResolutionService, çağıranın ambient CompanyId'si için bu
        // ilk satırı seçer, bu yüzden policy kontrolü hâlâ tenant-filtered
        // olsaydı sadece Company A'nın Trainer satırını görür ve Company B
        // için GymAdmin ile korunan her işlemi yanlışlıkla reddederdi.
        db.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = companyA.Id, Role = AssignmentRole.Trainer, IsActive = true });
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = companyB.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add(GymAppApi.WebApi.Middleware.TenantContextMiddleware.ActiveCompanyHeaderName, companyB.Id.ToString());

        var response = await client.PostAsJsonAsync("/api/branches", new { companyId = companyB.Id, name = "Merkez Şube", address = "Adres 1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
