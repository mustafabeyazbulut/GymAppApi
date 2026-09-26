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

    private static string UniquePhone() => $"+9055511{Random.Shared.Next(10000, 99999)}";

    private async Task<(int companyId, int memberUserId, string gymAdminToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Company", IsActive = true };
        db.Companies.Add(company);

        var member = new User { FullName = "Member User", Phone = UniquePhone(), PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin);
        await db.SaveChangesAsync();

        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;

        return (company.Id, member.Id, token);
    }

    // Senaryo §10.7: gym'e "üye ekleme" yok, Role=Member ataması sistemden
    // kaldırıldı - kullanıcıyı gym'e bağlayan tek işlem paket tanımlamak.
    // Eski POST /api/assignments (Member daveti) artık yok; aynı rota
    // üzerinde sadece GET (personel listesi) kaldığı için 405 döner.
    [Fact]
    public async Task PostToAssignmentsRoot_MemberInviteEndpointIsRemoved_Returns405()
    {
        var (companyId, memberUserId, gymAdminToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminToken);

        var response = await client.PostAsJsonAsync("/api/assignments", new { userId = memberUserId, companyId, branchId = (int?)null });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
