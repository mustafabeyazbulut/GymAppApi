using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class AddStaffMemberAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AddStaffMemberAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(int branchId, string branchManagerToken, string memberToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var branchManager = new User { FullName = "Branch Manager", Phone = "+905550004444", PasswordHash = "x" };
        var member = new User { FullName = "Plain Member", Phone = "+905550005555", PasswordHash = "x" };
        db.Users.AddRange(branchManager, member);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = branchManager.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.BranchManager, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var branchManagerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(branchManager.Id, branchManager.FullName, branchManager.Email, branchManager.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        return (branch.Id, branchManagerToken, memberToken);
    }

    [Fact]
    public async Task AddStaff_WithBranchManagerToken_Returns201()
    {
        var (branchId, branchManagerToken, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            fullName = "New Member",
            phone = "+905550006666",
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AddStaff_WithPlainMemberToken_Returns403()
    {
        var (branchId, _, memberToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            fullName = "New Member",
            phone = "+905550006666",
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
