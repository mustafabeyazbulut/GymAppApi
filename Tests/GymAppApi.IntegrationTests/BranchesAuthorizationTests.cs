using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class BranchesAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BranchesAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // This factory's DB is shared across every test method in this class -
    // a fresh random phone per call avoids one test's seed silently matching
    // a stale row from another test's SeedAsync() call.
    private static string UniquePhone() => $"+9055502{Random.Shared.Next(10000, 99999)}";

    private async Task<(Company companyA, string gymAdminAToken, string gymAdminBToken, string memberToken, string superAdminToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var companyA = new Company { Name = "Company A", IsActive = true };
        var companyB = new Company { Name = "Company B", IsActive = true };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var gymAdminA = new User { FullName = "Gym Admin A", Phone = UniquePhone(), PasswordHash = "x" };
        var gymAdminB = new User { FullName = "Gym Admin B", Phone = UniquePhone(), PasswordHash = "x" };
        var member = new User { FullName = "Plain Member", Phone = UniquePhone(), PasswordHash = "x" };
        var superAdmin = new User { FullName = "Super Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(gymAdminA, gymAdminB, member, superAdmin);
        await db.SaveChangesAsync();

        db.Assignments.AddRange(
            new Assignment { UserId = gymAdminA.Id, CompanyId = companyA.Id, Role = AssignmentRole.GymAdmin, IsActive = true },
            new Assignment { UserId = gymAdminB.Id, CompanyId = companyB.Id, Role = AssignmentRole.GymAdmin, IsActive = true },
            new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminAToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdminA.Id, gymAdminA.FullName, gymAdminA.Email, gymAdminA.Phone)).Token;
        var gymAdminBToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdminB.Id, gymAdminB.FullName, gymAdminB.Email, gymAdminB.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;
        var superAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;

        return (companyA, gymAdminAToken, gymAdminBToken, memberToken, superAdminToken);
    }

    private static object Body(int companyId) => new { companyId, name = "Merkez Şube", address = "Adres 1" };

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/branches", Body(1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithPlainMemberToken_Returns403()
    {
        var (companyA, _, _, memberToken, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsGymAdminOfOwnCompany_Returns201()
    {
        var (companyA, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);

        var response = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsGymAdminOfADifferentCompany_Returns404()
    {
        // Not 403: Company itself is tenant-scoped (SetCompanySelfFilter), so
        // a GymAdmin of a DIFFERENT company can't even see companyA exists -
        // CompanyMustExistAsync's own query already hides it, same as any
        // other cross-tenant read in this API. This avoids confirming
        // another tenant's existence to someone unauthorized for it.
        var (companyA, _, gymAdminBToken, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminBToken);

        var response = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    // Senaryo §10.6: Sistem Sahibi şube açmaz - firmanın içini Gym Admin kurar.
    public async Task Create_AsSuperAdmin_Returns403()
    {
        var (companyA, _, _, _, superAdminToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetActive_AsGymAdminOfOwnCompany_DeactivatesTheBranch()
    {
        var (companyA, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);
        var createResponse = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateBranchResultDto>();

        var response = await client.PatchAsJsonAsync($"/api/branches/{created!.Id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<GymAppApi.Infrastructure.Tenancy.AmbientTenantContext>().IsSuperAdmin = true;
        Assert.False(db.Branches.Single(b => b.Id == created.Id).IsActive);
    }

    [Fact]
    public async Task SetActive_AsGymAdminOfOwnCompany_CanReopenAClosedBranch()
    {
        // Senaryo §4.5: Gym Admin şube açar, düzenler, kapatır - kapattığı
        // şubeyi yeniden açabilmeli (eskiden bunu sadece filtreyi atlayan
        // SuperAdmin yapabiliyordu; §10.6 ile o yol da kapandı).
        var (companyA, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);
        var created = await (await client.PostAsJsonAsync("/api/branches", Body(companyA.Id))).Content.ReadFromJsonAsync<CreateBranchResultDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/branches/{created!.Id}/active", new { isActive = false })).StatusCode);

        var reopen = await client.PatchAsJsonAsync($"/api/branches/{created.Id}/active", new { isActive = true });

        Assert.Equal(HttpStatusCode.NoContent, reopen.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<GymAppApi.Infrastructure.Tenancy.AmbientTenantContext>().IsSuperAdmin = true;
        Assert.True(db.Branches.Single(b => b.Id == created.Id).IsActive);
    }

    [Fact]
    public async Task Update_AsGymAdminOfOwnCompany_RenamesTheBranch()
    {
        var (companyA, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);
        var createResponse = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateBranchResultDto>();

        var response = await client.PatchAsJsonAsync($"/api/branches/{created!.Id}", new { name = "Yeni Ad", address = "Yeni Adres" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<GymAppApi.Infrastructure.Tenancy.AmbientTenantContext>().IsSuperAdmin = true;
        var updated = db.Branches.Single(b => b.Id == created.Id);
        Assert.Equal("Yeni Ad", updated.Name);
        Assert.Equal("Yeni Adres", updated.Address);
    }

    [Fact]
    public async Task Update_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync("/api/branches/1", new { name = "Yeni Ad", address = "Yeni Adres" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AsGymAdminOfOwnCompany_ReturnsTheBranch()
    {
        var (companyA, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);
        var createResponse = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateBranchResultDto>();

        var response = await client.GetAsync($"/api/branches/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateBranchResultDto>();
        Assert.Equal(created.Id, body!.Id);
        Assert.Equal("Merkez Şube", body.Name);
    }

    [Fact]
    public async Task GetById_ForNonExistentId_Returns404()
    {
        var (_, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);

        var response = await client.GetAsync("/api/branches/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AsGymAdminOfADifferentCompany_Returns404()
    {
        // Same tenant-scoping story as GetAll: the global query filter on
        // Branch hides companyA's branch from gymAdminB entirely, so the
        // handler's GetAsync finds nothing and this surfaces as 404, not 403.
        var (companyA, gymAdminAToken, gymAdminBToken, _, _) = await SeedAsync();
        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);
        var createResponse = await ownerClient.PostAsJsonAsync("/api/branches", Body(companyA.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateBranchResultDto>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminBToken);
        var response = await client.GetAsync($"/api/branches/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/branches/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record CreateBranchResultDto(int Id, string Name);
}
