using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

public class AssignmentsAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AssignmentsAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(int companyId, int memberUserId, int gymAdminUserId, string gymAdminToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Company", IsActive = true };
        db.Companies.Add(company);

        var member = new User { FullName = "Member User", Phone = "+905550000001", PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = "+905550000002", PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin);
        await db.SaveChangesAsync();

        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<GymAppApi.Application.Common.Interfaces.IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;

        return (company.Id, member.Id, gymAdmin.Id, token);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var (companyId, memberUserId, _, _) = await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/assignments", new { userId = memberUserId, companyId, branchId = (int?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithMemberToken_Returns403()
    {
        var (companyId, memberUserId, _, _) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<GymAppApi.Application.Common.Interfaces.IJwtTokenService>();
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(memberUserId, "Member User", null, "+905550000001")).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/assignments", new { userId = memberUserId, companyId, branchId = (int?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithGymAdminToken_Returns201()
    {
        var (companyId, memberUserId, _, gymAdminToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminToken);

        var response = await client.PostAsJsonAsync("/api/assignments", new { userId = memberUserId, companyId, branchId = (int?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Regression test for the cross-company privilege-escalation fix
    // (f39c638): a GymAdmin of one company must not be able to assign
    // members into an unrelated company. This is also the first real-host
    // exercise of CreateAssignmentCommandHandler's own ForbiddenException ->
    // ExceptionMiddleware -> 403 path (the other tests' 401/403 come from
    // ASP.NET Core's built-in auth challenge/forbid, not a thrown exception).
    [Fact]
    public async Task Create_WithGymAdminTokenForDifferentCompany_Returns403()
    {
        var (_, memberUserId, _, gymAdminToken) = await SeedAsync();

        int otherCompanyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
            var otherCompany = new Company { Name = "Other Company", IsActive = true };
            db.Companies.Add(otherCompany);
            await db.SaveChangesAsync();
            otherCompanyId = otherCompany.Id;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminToken);

        var response = await client.PostAsJsonAsync("/api/assignments", new { userId = memberUserId, companyId = otherCompanyId, branchId = (int?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
