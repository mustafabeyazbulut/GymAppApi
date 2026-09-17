using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class TenantContextMiddlewareTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TenantContextMiddlewareTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetBranches_AsGymAdmin_OnlySeesOwnCompanyBranches()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var companyA = new Company { Name = "Company A", IsActive = true };
        var companyB = new Company { Name = "Company B", IsActive = true };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        db.Branches.AddRange(
            new Branch { CompanyId = companyA.Id, Name = "A - Merkez", Address = "..." },
            new Branch { CompanyId = companyB.Id, Name = "B - Merkez", Address = "..." });
        await db.SaveChangesAsync();

        var gymAdmin = new User { FullName = "Gym Admin A", Phone = "+905550000020", PasswordHash = "x" };
        db.Users.Add(gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = companyA.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/branches");
        response.EnsureSuccessStatusCode();
        var branches = await response.Content.ReadFromJsonAsync<List<BranchDto>>();

        Assert.NotNull(branches);
        Assert.Single(branches!);
        Assert.Equal("A - Merkez", branches![0].Name);
    }

    private record BranchDto(int Id, string Name, string Address, bool IsActive);
}
