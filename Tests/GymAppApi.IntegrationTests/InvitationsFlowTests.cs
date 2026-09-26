using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Adım 4 - "Davetlerim": kullanıcı kendi bekleyen davetlerini listeler ve
// SMS kodu olmadan uygulama içinden kabul/red eder. Kabulün iş kuralları SMS
// kodlu /confirm ile ortak (AssignmentInvitationAcceptance /
// PackageInvitationAcceptance).
public class InvitationsFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InvitationsFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055514{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(
        int CompanyId, int BranchId, int PackageId,
        string GymAdminToken, int GymAdminUserId,
        string CandidateToken, string CandidatePhone, int CandidateUserId,
        string OtherUserToken);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Firma Davet", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez Şube", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "Aylık", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        db.Packages.Add(package);

        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x", PhoneVerified = true, PreferredLanguage = "tr" };
        var candidate = new User { FullName = "Aday Kişi", Phone = UniquePhone(), PasswordHash = "x", PhoneVerified = true };
        var other = new User { FullName = "Başka Kişi", Phone = UniquePhone(), PasswordHash = "x", PhoneVerified = true };
        db.Users.AddRange(gymAdmin, candidate, other);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return new Seed(company.Id, branch.Id, package.Id, Token(gymAdmin), gymAdmin.Id, Token(candidate), candidate.Phone, candidate.Id, Token(other));
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<List<JsonElement>> GetMyInvitationsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/invitations/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private async Task InviteAsTrainerAsync(Seed seed) =>
        Assert.Equal(HttpStatusCode.Created, (await ClientFor(seed.GymAdminToken).PostAsJsonAsync("/api/assignments/staff",
            new { phone = seed.CandidatePhone, role = "Trainer", branchId = seed.BranchId })).StatusCode);

    [Fact]
    public async Task StaffInvitation_IsListedOnlyForTheInvitee_WithDetails()
    {
        var seed = await SeedAsync();
        await InviteAsTrainerAsync(seed);

        var mine = await GetMyInvitationsAsync(ClientFor(seed.CandidateToken));
        var others = await GetMyInvitationsAsync(ClientFor(seed.OtherUserToken));

        var invitation = Assert.Single(mine);
        Assert.Equal("Staff", invitation.GetProperty("type").GetString());
        Assert.Equal("Firma Davet", invitation.GetProperty("companyName").GetString());
        Assert.Equal("Merkez Şube", invitation.GetProperty("branchName").GetString());
        Assert.Equal("Trainer", invitation.GetProperty("role").GetString());
        Assert.Equal("Gym Admin", invitation.GetProperty("invitedByName").GetString());
        Assert.Equal(JsonValueKind.Null, invitation.GetProperty("packageName").ValueKind);
        Assert.Empty(others);
    }

    [Fact]
    public async Task AcceptStaffInvitation_CreatesTheAssignment_VisibleInGetMe_AndRemovesItFromTheList()
    {
        var seed = await SeedAsync();
        await InviteAsTrainerAsync(seed);
        var client = ClientFor(seed.CandidateToken);
        var id = (await GetMyInvitationsAsync(client)).Single().GetProperty("id").GetInt32();

        var accept = await client.PostAsync($"/api/invitations/staff/{id}/accept", null);

        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(me.GetProperty("assignments").EnumerateArray(), a =>
            a.GetProperty("role").GetString() == "Trainer" && a.GetProperty("branchId").GetInt32() == seed.BranchId);
        Assert.Empty(await GetMyInvitationsAsync(client));
    }

    [Fact]
    public async Task AcceptPackageInvitation_CreatesThePackageAssignment_VisibleInGetMe()
    {
        var seed = await SeedAsync();
        Assert.Equal(HttpStatusCode.Created, (await ClientFor(seed.GymAdminToken).PostAsJsonAsync("/api/package-assignments",
            new { packageId = seed.PackageId, memberPhone = seed.CandidatePhone })).StatusCode);
        var client = ClientFor(seed.CandidateToken);
        var invitation = (await GetMyInvitationsAsync(client)).Single();
        Assert.Equal("Package", invitation.GetProperty("type").GetString());
        Assert.Equal("Aylık", invitation.GetProperty("packageName").GetString());

        var accept = await client.PostAsync($"/api/invitations/package/{invitation.GetProperty("id").GetInt32()}/accept", null);

        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(me.GetProperty("packageAssignments").EnumerateArray(), pa => pa.GetProperty("packageId").GetInt32() == seed.PackageId);
    }

    [Fact]
    public async Task RejectInvitation_RemovesIt_AndNotifiesTheInviterInTheirLanguage()
    {
        var seed = await SeedAsync();
        await InviteAsTrainerAsync(seed);
        var client = ClientFor(seed.CandidateToken);
        var id = (await GetMyInvitationsAsync(client)).Single().GetProperty("id").GetInt32();

        var reject = await client.PostAsync($"/api/invitations/staff/{id}/reject", null);

        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);
        Assert.Empty(await GetMyInvitationsAsync(client));
        var inviterNotifications = await (await ClientFor(seed.GymAdminToken).GetAsync("/api/notifications")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(inviterNotifications.EnumerateArray(), n =>
            n.GetProperty("title").GetString()!.Contains("reddedildi") && n.GetProperty("body").GetString()!.Contains("Aday Kişi"));
    }

    [Fact]
    public async Task AnotherUsersInvitation_Returns404_ForAcceptAndReject()
    {
        var seed = await SeedAsync();
        await InviteAsTrainerAsync(seed);
        var id = (await GetMyInvitationsAsync(ClientFor(seed.CandidateToken))).Single().GetProperty("id").GetInt32();
        var other = ClientFor(seed.OtherUserToken);

        var accept = await other.PostAsync($"/api/invitations/staff/{id}/accept", null);
        var reject = await other.PostAsync($"/api/invitations/staff/{id}/reject", null);

        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal("InvitationNotFound", JsonDocument.Parse(await accept.Content.ReadAsStringAsync()).RootElement.GetProperty("Code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
    }

    [Fact]
    public async Task ExpiredInvitation_Returns410()
    {
        var seed = await SeedAsync();
        int invitationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
            var expired = new PendingPackageAssignmentInvitation
            {
                TargetUserId = seed.CandidateUserId, PackageId = seed.PackageId, CompanyId = seed.CompanyId, BranchId = seed.BranchId,
                RequestedByUserId = seed.GymAdminUserId, Code = "111111", ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            };
            db.PendingPackageAssignmentInvitations.Add(expired);
            await db.SaveChangesAsync();
            invitationId = expired.Id;
        }

        var response = await ClientFor(seed.CandidateToken).PostAsync($"/api/invitations/package/{invitationId}/accept", null);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("InvitationExpired", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("Code").GetString());
    }

    [Fact]
    public async Task AcceptPath_EnforcesTheConflictingRoleRule()
    {
        // Aday bu firmada zaten Şube Yöneticisi; aynı firmaya GymAdmin daveti
        // kabul yolunda da reddedilir (SMS kodlu confirm ile aynı kural).
        var seed = await SeedAsync();
        int invitationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
            db.Assignments.Add(new Assignment { UserId = seed.CandidateUserId, CompanyId = seed.CompanyId, BranchId = seed.BranchId, Role = AssignmentRole.BranchManager, IsActive = true });
            var invitation = new PendingAssignmentInvitation
            {
                TargetUserId = seed.CandidateUserId, CompanyId = seed.CompanyId, BranchId = null, Role = AssignmentRole.GymAdmin,
                RequestedByUserId = seed.GymAdminUserId, Code = "222222", ExpiresAt = DateTime.UtcNow.AddDays(1),
            };
            db.PendingAssignmentInvitations.Add(invitation);
            await db.SaveChangesAsync();
            invitationId = invitation.Id;
        }

        var response = await ClientFor(seed.CandidateToken).PostAsync($"/api/invitations/gymadmin/{invitationId}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ConflictingAssignmentRole", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("Code").GetString());
    }

    [Fact]
    public async Task InvitationTypeMismatch_Returns404()
    {
        var seed = await SeedAsync();
        await InviteAsTrainerAsync(seed);
        var client = ClientFor(seed.CandidateToken);
        var id = (await GetMyInvitationsAsync(client)).Single().GetProperty("id").GetInt32();

        // Trainer daveti "gymadmin" tipiyle kabul edilemez.
        var response = await client.PostAsync($"/api/invitations/gymadmin/{id}/accept", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
