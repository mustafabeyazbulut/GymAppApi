using System.Net;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class RemoveAssignmentAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RemoveAssignmentAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055504{Random.Shared.Next(10000, 99999)}";

    [Fact]
    public async Task Remove_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync("/api/assignments/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Remove_LastGymAdminAsAnotherGymAdmin_Returns409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var onlyGymAdmin = new User { FullName = "Only Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(onlyGymAdmin);
        await db.SaveChangesAsync();
        var onlyGymAdminAssignment = new Assignment { UserId = onlyGymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        db.Assignments.Add(onlyGymAdminAssignment);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(onlyGymAdmin.Id, onlyGymAdmin.FullName, onlyGymAdmin.Email, onlyGymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.DeleteAsync($"/api/assignments/{onlyGymAdminAssignment.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Remove_SecondGymAdminAsFirstGymAdmin_Returns204()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var firstAdmin = new User { FullName = "First Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var secondAdmin = new User { FullName = "Second Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(firstAdmin, secondAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = firstAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        var secondAssignment = new Assignment { UserId = secondAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        db.Assignments.Add(secondAssignment);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var firstAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(firstAdmin.Id, firstAdmin.FullName, firstAdmin.Email, firstAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", firstAdminToken);
        var response = await client.DeleteAsync($"/api/assignments/{secondAssignment.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // Fresh scope so this read hits the store instead of returning the
        // stale tracked instance still held by the seeding "db"/"scope"
        // above (matches the pattern used elsewhere, e.g.
        // BranchesAuthorizationTests.SetActive_AsGymAdminOfOwnCompany_DeactivatesTheBranch).
        using var assertScope = _factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        assertScope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        Assert.False(assertDb.Assignments.Single(a => a.Id == secondAssignment.Id).IsActive);
    }
}
