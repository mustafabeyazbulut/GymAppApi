using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class PackageAssignmentFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PackageAssignmentFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055502{Random.Shared.Next(10000, 99999)}";

    private async Task<(int companyId, int branchId, int memberId, string memberPhone, string gymAdminToken, string memberToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var memberPhone = UniquePhone();
        var member = new User { FullName = "Member", Phone = memberPhone, PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        return (company.Id, branch.Id, member.Id, memberPhone, gymAdminToken, memberToken);
    }

    [Fact]
    public async Task FullFlow_CreatePackage_Assign_Confirm_ShowsUpOnGetMe()
    {
        var (companyId, branchId, memberId, memberPhone, gymAdminToken, memberToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);

        var createResponse = await client.PostAsJsonAsync("/api/packages", new
        {
            companyId,
            branchId,
            name = "10 Seans",
            type = "SessionBased",
            sessionCount = 10,
            price = 1000,
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var packageId = created.GetProperty("id").GetInt32();

        var assignResponse = await client.PostAsJsonAsync("/api/package-assignments", new { packageId, memberPhone });
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var code = db.PendingPackageAssignmentInvitations.Single(p => p.TargetUserId == memberId).Code;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/package-assignments/confirm", new { code });
        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var packageAssignments = me.GetProperty("packageAssignments");
        Assert.Equal(1, packageAssignments.GetArrayLength());
        Assert.Equal(companyId, packageAssignments[0].GetProperty("companyId").GetInt32());
        Assert.Equal("10 Seans", packageAssignments[0].GetProperty("packageName").GetString());
    }

    [Fact]
    public async Task Create_WithBranchManagerTokenForAnotherBranch_Returns403()
    {
        var (companyId, branchId, _, _, gymAdminToken, _) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var otherBranch = new Branch { CompanyId = companyId, Name = "İkinci Şube", Address = "..." };
        db.Branches.Add(otherBranch);
        var branchManager = new User { FullName = "Branch Manager", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(branchManager);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = branchManager.Id, CompanyId = companyId, BranchId = otherBranch.Id, Role = AssignmentRole.BranchManager, IsActive = true });
        await db.SaveChangesAsync();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var branchManagerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(branchManager.Id, branchManager.FullName, branchManager.Email, branchManager.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", branchManagerToken);
        var response = await client.PostAsJsonAsync("/api/packages", new
        {
            companyId,
            branchId,
            name = "10 Seans",
            type = "SessionBased",
            sessionCount = 10,
            price = 1000,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
