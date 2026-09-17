using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class CompaniesAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CompaniesAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static object ValidBody() => new
    {
        companyName = "New Gym",
        branchName = "Merkez",
        branchAddress = "Adres 1",
        gymAdminFullName = "Ada Admin",
        gymAdminPhone = "+905559998877",
        gymAdminEmail = (string?)null,
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
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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

    private record CompanyListItemDto(int Id, string Name, bool IsActive, int BranchCount);

    private record CompanyDetailDto(int Id, string Name, bool IsActive, List<BranchListItemDto> Branches);

    private record BranchListItemDto(int Id, int CompanyId, string Name, string Address, bool IsActive);
}
