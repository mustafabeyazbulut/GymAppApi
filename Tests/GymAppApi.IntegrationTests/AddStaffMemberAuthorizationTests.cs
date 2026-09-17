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

public class AddStaffMemberAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AddStaffMemberAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // The candidate's phone must be unique per call - AddStaffMemberCommandHandler
    // looks the target up with a plain FirstOrDefaultAsync (not Single), so if
    // another test's SeedAsync() call already left a row behind with the same
    // phone (this factory's DB is shared across every test in the class), the
    // handler could silently match THAT stale user instead of this test's own.
    private static string UniqueCandidatePhone() => $"+9055501{Random.Shared.Next(10000, 99999)}";

    private async Task<(int branchId, int newStaffCandidateId, string newStaffCandidatePhone, string branchManagerToken, string memberToken, string newStaffCandidateToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var candidatePhone = UniqueCandidatePhone();
        var branchManager = new User { FullName = "Branch Manager", Phone = "+905550004444", PasswordHash = "x" };
        var member = new User { FullName = "Plain Member", Phone = "+905550005555", PasswordHash = "x" };
        var newStaffCandidate = new User { FullName = "New Member", Phone = candidatePhone, PasswordHash = "x" };
        db.Users.AddRange(branchManager, member, newStaffCandidate);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = branchManager.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.BranchManager, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var branchManagerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(branchManager.Id, branchManager.FullName, branchManager.Email, branchManager.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;
        var newStaffCandidateToken = jwtService.GenerateAccessToken(new AccessTokenClaims(newStaffCandidate.Id, newStaffCandidate.FullName, newStaffCandidate.Email, newStaffCandidate.Phone)).Token;

        return (branch.Id, newStaffCandidate.Id, candidatePhone, branchManagerToken, memberToken, newStaffCandidateToken);
    }

    [Fact]
    public async Task AddStaff_WithBranchManagerToken_Returns201()
    {
        var (branchId, _, candidatePhone, branchManagerToken, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            phone = candidatePhone,
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AddStaff_DoesNotCreateTheAssignmentUntilTheCandidateConfirmsTheirOwnCode()
    {
        var (branchId, candidateId, candidatePhone, branchManagerToken, _, newStaffCandidateToken) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        // Assignment is ITenantScoped - this scope's own AmbientTenantContext
        // was never populated by TenantContextMiddleware (that only runs for
        // real HTTP requests), so it defaults fail-closed and would hide any
        // company-scoped row regardless of whether one exists. Flip it to
        // SuperAdmin to get an honest read for these assertions.
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);
        var addResponse = await client.PostAsJsonAsync("/api/assignments/staff", new { phone = candidatePhone, role = "Member", branchId });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        Assert.False(db.Assignments.Any(a => a.UserId == candidateId));

        var code = db.PendingAssignmentInvitations.Single(p => p.TargetUserId == candidateId).Code;

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newStaffCandidateToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/assignments/confirm", new { code });

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        var assignment = db.Assignments.Single(a => a.UserId == candidateId);
        Assert.Equal(branchId, assignment.BranchId);
        Assert.Equal(AssignmentRole.Member, assignment.Role);
    }

    [Fact]
    public async Task Confirm_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/assignments/confirm", new { code = "123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_WithAWrongCode_Returns401AndCreatesNoAssignment()
    {
        var (branchId, candidateId, candidatePhone, branchManagerToken, _, newStaffCandidateToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);
        await client.PostAsJsonAsync("/api/assignments/staff", new { phone = candidatePhone, role = "Member", branchId });

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newStaffCandidateToken);
        var response = await client.PostAsJsonAsync("/api/assignments/confirm", new { code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        Assert.False(db.Assignments.Any(a => a.UserId == candidateId));
    }

    [Fact]
    public async Task AddStaff_WhenPhoneIsNotARegisteredUser_Returns404()
    {
        var (branchId, _, _, branchManagerToken, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            phone = "+905550007777",
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddStaff_WithPlainMemberToken_Returns403()
    {
        var (branchId, _, candidatePhone, _, memberToken, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            phone = candidatePhone,
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
