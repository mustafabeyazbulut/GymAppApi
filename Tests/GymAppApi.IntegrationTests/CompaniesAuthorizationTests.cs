using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class CompaniesAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CompaniesAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static object ValidBody(string gymAdminPhone = "+905559998877") => new
    {
        companyName = "New Gym",
        gymAdminPhone,
    };

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithGymAdminToken_Returns403()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Existing Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var gymAdmin = new User { FullName = "Gym Admin", Phone = "+905550001111", PasswordHash = "x" };
        db.Users.Add(gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithSuperAdminToken_Returns201()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var superAdmin = new User { FullName = "Super Admin", Phone = "+905550002222", PasswordHash = "x" };
        var futureGymAdmin = new User { FullName = "Future Gym Admin", Phone = "+905559998877", PasswordHash = "x" };
        db.Users.AddRange(superAdmin, futureGymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var notification = db.Notifications.Single(n => n.UserId == futureGymAdmin.Id);
        Assert.False(notification.IsRead);

        // Assignment is ITenantScoped - this scope's own AmbientTenantContext
        // was never populated by TenantContextMiddleware (that only runs for
        // real HTTP requests), so it defaults fail-closed and would hide any
        // company-scoped row regardless of whether one exists. Flip it to
        // SuperAdmin to get an honest read for this assertion.
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        Assert.False(db.Assignments.Any(a => a.UserId == futureGymAdmin.Id));
    }

    [Fact]
    public async Task Create_ThenConfirmWithTheInvitedUsersOwnCode_CreatesTheGymAdminAssignment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var superAdmin = new User { FullName = "Super Admin", Phone = "+905550002244", PasswordHash = "x" };
        var futureGymAdmin = new User { FullName = "Future Gym Admin", Phone = "+905559998866", PasswordHash = "x" };
        db.Users.AddRange(superAdmin, futureGymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var superAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(futureGymAdmin.Id, futureGymAdmin.FullName, futureGymAdmin.Email, futureGymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", superAdminToken);
        var createResponse = await client.PostAsJsonAsync("/api/companies", ValidBody(gymAdminPhone: "+905559998866"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateCompanyResultDto>();

        var code = db.PendingAssignmentInvitations.Single(p => p.TargetUserId == futureGymAdmin.Id).Code;

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/assignments/confirm", new { code });

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);

        // See the AmbientTenantContext note in Create_WithSuperAdminToken_Returns201.
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var assignment = db.Assignments.Single(a => a.UserId == futureGymAdmin.Id);
        Assert.Equal(created!.CompanyId, assignment.CompanyId);
        Assert.Equal(AssignmentRole.GymAdmin, assignment.Role);
    }

    [Fact]
    public async Task Create_WhenGymAdminPhoneIsNotARegisteredUser_Returns404()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var superAdmin = new User { FullName = "Super Admin", Phone = "+905550002233", PasswordHash = "x" };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody(gymAdminPhone: "+905550009999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Company company, Branch branch, string superAdminToken)> SeedCompanyAndSuperAdminAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Existing Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "Adres 1" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var superAdmin = new User { FullName = "Super Admin", Phone = "+905550003333", PasswordHash = "x" };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;

        return (company, branch, token);
    }

    [Fact]
    public async Task GetAll_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithSuperAdminToken_ReturnsCompaniesWithBranchCount()
    {
        var (company, _, token) = await SeedCompanyAndSuperAdminAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var companies = await response.Content.ReadFromJsonAsync<List<CompanyListItemDto>>();
        var found = Assert.Single(companies!, c => c.Id == company.Id);
        Assert.Equal(1, found.BranchCount);
    }

    [Fact]
    public async Task GetById_WithSuperAdminToken_ReturnsCompanyWithBranches()
    {
        var (company, branch, token) = await SeedCompanyAndSuperAdminAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/companies/{company.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<CompanyDetailDto>();
        Assert.Equal(company.Name, detail!.Name);
        var returnedBranch = Assert.Single(detail.Branches);
        Assert.Equal(branch.Name, returnedBranch.Name);
    }

    [Fact]
    public async Task UpdateName_WithSuperAdminToken_UpdatesTheCompanysName()
    {
        var (company, _, token) = await SeedCompanyAndSuperAdminAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync($"/api/companies/{company.Id}", new { name = "Renamed Co" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getResponse = await client.GetAsync($"/api/companies/{company.Id}");
        var detail = await getResponse.Content.ReadFromJsonAsync<CompanyDetailDto>();
        Assert.Equal("Renamed Co", detail!.Name);
    }

    [Fact]
    public async Task SetActive_WithSuperAdminToken_DeactivatesAndReactivatesTheCompany()
    {
        var (company, _, token) = await SeedCompanyAndSuperAdminAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var deactivateResponse = await client.PatchAsJsonAsync($"/api/companies/{company.Id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{company.Id}");
        var detail = await getResponse.Content.ReadFromJsonAsync<CompanyDetailDto>();
        Assert.False(detail!.IsActive);
    }

    private record CreateCompanyResultDto(int CompanyId, int GymAdminUserId, string GymAdminPhone);

    private record CompanyListItemDto(int Id, string Name, bool IsActive, int BranchCount);

    private record CompanyDetailDto(int Id, string Name, bool IsActive, List<BranchListItemDto> Branches);

    private record BranchListItemDto(int Id, int CompanyId, string Name, string Address, bool IsActive);
}
