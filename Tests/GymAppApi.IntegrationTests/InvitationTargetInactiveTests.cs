using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Canlı test bulgusu: kapatılmış şubeye yapılmış personel daveti hâlâ kabul
// ediliyordu. Kabulde (uygulama içi ve SMS kodu yolu) şube/firma ve paket
// davetinde paket de aktif olmalı; değilse 409 InvitationTargetInactive ve
// davet yanmaz.
public class InvitationTargetInactiveTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Code = "123456";
    private readonly CustomWebApplicationFactory _factory;

    public InvitationTargetInactiveTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055524{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(int CandidateId, int StaffInvitationId, int PackageInvitationId, string CandidateToken);

    private async Task<Seed> SeedAsync(bool companyActive = true, bool branchActive = true, bool packageActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = companyActive };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "...", IsActive = branchActive };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var package = new Package
        {
            CompanyId = company.Id, BranchId = branch.Id, Name = "Aylık", Type = PackageType.Duration, DurationDays = 30, Price = 100m, IsActive = packageActive,
        };
        db.Packages.Add(package);
        var inviter = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x", PhoneVerified = true };
        var candidate = new User { FullName = "Aday", Phone = UniquePhone(), PasswordHash = "x", PhoneVerified = true };
        db.Users.AddRange(inviter, candidate);
        await db.SaveChangesAsync();

        var staffInvitation = new PendingAssignmentInvitation
        {
            TargetUserId = candidate.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.Trainer,
            RequestedByUserId = inviter.Id, Code = Code, ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        var packageInvitation = new PendingPackageAssignmentInvitation
        {
            TargetUserId = candidate.Id, PackageId = package.Id, CompanyId = company.Id, BranchId = branch.Id,
            RequestedByUserId = inviter.Id, Code = Code, ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        db.PendingAssignmentInvitations.Add(staffInvitation);
        db.PendingPackageAssignmentInvitations.Add(packageInvitation);
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(new AccessTokenClaims(candidate.Id, candidate.FullName, candidate.Email, candidate.Phone)).Token;
        return new Seed(candidate.Id, staffInvitation.Id, packageInvitation.Id, token);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertInactiveTargetAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("InvitationTargetInactive", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());
    }

    private async Task AssertStaffInvitationNotBurnedAsync(Seed seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        Assert.False((await db.PendingAssignmentInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == seed.StaffInvitationId)).IsUsed);
        Assert.False(await db.Assignments.IgnoreQueryFilters().AnyAsync(a => a.UserId == seed.CandidateId));
    }

    private async Task AssertPackageInvitationNotBurnedAsync(Seed seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        Assert.False((await db.PendingPackageAssignmentInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == seed.PackageInvitationId)).IsUsed);
        Assert.False(await db.PackageAssignments.IgnoreQueryFilters().AnyAsync(pa => pa.MemberUserId == seed.CandidateId));
    }

    [Fact]
    public async Task StaffInvitation_ToAClosedBranch_InApp_Returns409_AndIsNotBurned()
    {
        var seed = await SeedAsync(branchActive: false);

        await AssertInactiveTargetAsync(await ClientFor(seed.CandidateToken).PostAsync($"/api/invitations/staff/{seed.StaffInvitationId}/accept", null));

        await AssertStaffInvitationNotBurnedAsync(seed);
    }

    [Fact]
    public async Task StaffInvitation_ToAClosedBranch_BySmsCode_Returns409_AndIsNotBurned()
    {
        var seed = await SeedAsync(branchActive: false);

        await AssertInactiveTargetAsync(await ClientFor(seed.CandidateToken).PostAsJsonAsync("/api/assignments/confirm", new { code = Code }));

        await AssertStaffInvitationNotBurnedAsync(seed);
    }

    [Fact]
    public async Task StaffInvitation_ToAnInactiveCompany_Returns409()
    {
        var seed = await SeedAsync(companyActive: false);

        await AssertInactiveTargetAsync(await ClientFor(seed.CandidateToken).PostAsync($"/api/invitations/staff/{seed.StaffInvitationId}/accept", null));
    }

    [Fact]
    public async Task PackageInvitation_ForAnInactivePackage_InApp_Returns409_AndIsNotBurned()
    {
        var seed = await SeedAsync(packageActive: false);

        await AssertInactiveTargetAsync(await ClientFor(seed.CandidateToken).PostAsync($"/api/invitations/package/{seed.PackageInvitationId}/accept", null));

        await AssertPackageInvitationNotBurnedAsync(seed);
    }

    [Fact]
    public async Task PackageInvitation_AtAClosedBranch_BySmsCode_Returns409_AndIsNotBurned()
    {
        var seed = await SeedAsync(branchActive: false);

        await AssertInactiveTargetAsync(await ClientFor(seed.CandidateToken).PostAsJsonAsync("/api/package-assignments/confirm", new { code = Code }));

        await AssertPackageInvitationNotBurnedAsync(seed);
    }

    [Fact]
    public async Task ActiveTargets_StillAccept()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.CandidateToken);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/invitations/staff/{seed.StaffInvitationId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/invitations/package/{seed.PackageInvitationId}/accept", null)).StatusCode);
    }
}
