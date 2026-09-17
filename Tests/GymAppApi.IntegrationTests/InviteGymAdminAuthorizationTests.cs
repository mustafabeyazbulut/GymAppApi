using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class InviteGymAdminAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InviteGymAdminAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055503{Random.Shared.Next(10000, 99999)}";

    private async Task<(int companyId, string gymAdminToken, string memberToken, string candidatePhone)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var member = new User { FullName = "Plain Member", Phone = UniquePhone(), PasswordHash = "x" };
        var candidate = new User { FullName = "Future Peer Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(gymAdmin, member, candidate);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        return (company.Id, gymAdminToken, memberToken, candidate.Phone);
    }

    [Fact]
    public async Task Invite_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId = 1, phone = "+905550000001" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invite_WithPlainMemberToken_Returns403()
    {
        var (companyId, _, memberToken, candidatePhone) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId, phone = candidatePhone });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_ThenConfirm_AsGymAdminOfOwnCompany_CreatesAPeerGymAdminAssignment()
    {
        var (companyId, gymAdminToken, _, candidatePhone) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminToken);
        var inviteResponse = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId, phone = candidatePhone });
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);

        var candidate = db.Users.Single(u => u.Phone == candidatePhone);
        var code = db.PendingAssignmentInvitations.Single(p => p.TargetUserId == candidate.Id).Code;
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var candidateToken = jwtService.GenerateAccessToken(new AccessTokenClaims(candidate.Id, candidate.FullName, candidate.Email, candidate.Phone)).Token;

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", candidateToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/assignments/confirm", new { code });

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        scope.ServiceProvider.GetRequiredService<GymAppApi.Infrastructure.Tenancy.AmbientTenantContext>().IsSuperAdmin = true;
        var assignment = db.Assignments.Single(a => a.UserId == candidate.Id);
        Assert.Equal(companyId, assignment.CompanyId);
        Assert.Equal(AssignmentRole.GymAdmin, assignment.Role);
        Assert.Null(assignment.BranchId);
    }
}
