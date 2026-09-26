using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using GymAppApi.WebApi.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Adım 2 - aktif atama bağlamı: mobil her istekte X-Active-Assignment-Id
// gönderir; tenant kapsamı (firma/şube) ve politika kararları o atamaya göre
// verilir. Gerçek HTTP pipeline'ı üzerinden uçtan uca doğrulanır.
public class ActiveAssignmentContextTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ActiveAssignmentContextTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055508{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(
        int CompanyAId, int BranchA1Id, int BranchA2Id, int CompanyBId,
        int TrainerA1AssignmentId, int ManagerA2AssignmentId, int GymAdminBAssignmentId, int InactiveAssignmentId,
        int OtherUsersAssignmentId,
        string MultiRoleToken, string SingleRoleToken, int SingleRoleBranchId);

    // Çok rollü kullanıcı: A1'de Trainer, A2'de BranchManager, B'de GymAdmin,
    // A1'de pasif bir BranchManager ataması. Ayrıca tek atamalı bir kullanıcı
    // ve başka bir kullanıcının ataması.
    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var companyA = new Company { Name = "Firma A", IsActive = true };
        var companyB = new Company { Name = "Firma B", IsActive = true };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var branchA1 = new Branch { CompanyId = companyA.Id, Name = "A1", Address = "..." };
        var branchA2 = new Branch { CompanyId = companyA.Id, Name = "A2", Address = "..." };
        var branchB1 = new Branch { CompanyId = companyB.Id, Name = "B1", Address = "..." };
        db.Branches.AddRange(branchA1, branchA2, branchB1);
        await db.SaveChangesAsync();

        var multiRole = new User { FullName = "Çok Rollü", Phone = UniquePhone(), PasswordHash = "x" };
        var singleRole = new User { FullName = "Tek Rollü", Phone = UniquePhone(), PasswordHash = "x" };
        var other = new User { FullName = "Başkası", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(multiRole, singleRole, other);
        await db.SaveChangesAsync();

        var trainerA1 = new Assignment { UserId = multiRole.Id, CompanyId = companyA.Id, BranchId = branchA1.Id, Role = AssignmentRole.Trainer, IsActive = true };
        var managerA2 = new Assignment { UserId = multiRole.Id, CompanyId = companyA.Id, BranchId = branchA2.Id, Role = AssignmentRole.BranchManager, IsActive = true };
        var gymAdminB = new Assignment { UserId = multiRole.Id, CompanyId = companyB.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        var inactive = new Assignment { UserId = multiRole.Id, CompanyId = companyA.Id, BranchId = branchA1.Id, Role = AssignmentRole.BranchManager, IsActive = false };
        var singleManagerB1 = new Assignment { UserId = singleRole.Id, CompanyId = companyB.Id, BranchId = branchB1.Id, Role = AssignmentRole.BranchManager, IsActive = true };
        var othersGymAdminA = new Assignment { UserId = other.Id, CompanyId = companyA.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        db.Assignments.AddRange(trainerA1, managerA2, gymAdminB, inactive, singleManagerB1, othersGymAdminA);
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;

        return new Seed(
            companyA.Id, branchA1.Id, branchA2.Id, companyB.Id,
            trainerA1.Id, managerA2.Id, gymAdminB.Id, inactive.Id,
            othersGymAdminA.Id,
            Token(multiRole), Token(singleRole), branchB1.Id);
    }

    private HttpClient ClientFor(string token, int? activeAssignmentId = null, string? rawHeader = null, string? language = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (activeAssignmentId is not null)
        {
            client.DefaultRequestHeaders.Add(TenantContextMiddleware.ActiveAssignmentHeaderName, activeAssignmentId.ToString());
        }
        if (rawHeader is not null)
        {
            client.DefaultRequestHeaders.Add(TenantContextMiddleware.ActiveAssignmentHeaderName, rawHeader);
        }
        if (language is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        }
        return client;
    }

    private static async Task<int[]> GetBranchIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/branches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        return items.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).OrderBy(id => id).ToArray();
    }

    [Fact]
    public async Task SameCompany_TrainerInA1_ManagerInA2_ScopeFollowsTheHeader()
    {
        var seed = await SeedAsync();

        Assert.Equal(new[] { seed.BranchA1Id }, await GetBranchIdsAsync(ClientFor(seed.MultiRoleToken, seed.TrainerA1AssignmentId)));
        Assert.Equal(new[] { seed.BranchA2Id }, await GetBranchIdsAsync(ClientFor(seed.MultiRoleToken, seed.ManagerA2AssignmentId)));
    }

    [Fact]
    public async Task WithoutHeader_SingleAssignment_UsesIt()
    {
        var seed = await SeedAsync();

        Assert.Equal(new[] { seed.SingleRoleBranchId }, await GetBranchIdsAsync(ClientFor(seed.SingleRoleToken)));
    }

    [Fact]
    public async Task AnotherUsersAssignmentId_Returns403WithLocalizedCode()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.MultiRoleToken, seed.OtherUsersAssignmentId, language: "tr").GetAsync("/api/branches");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvalidActiveAssignment", body.RootElement.GetProperty("Code").GetString());
        Assert.Contains("Seçili rol", body.RootElement.GetProperty("Errors")[0].GetString());
    }

    [Fact]
    public async Task OwnInactiveAssignmentId_Returns403()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.MultiRoleToken, seed.InactiveAssignmentId).GetAsync("/api/branches");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonNumericHeader_Returns403()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.MultiRoleToken, rawHeader: "abc").GetAsync("/api/branches");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Policy_ActiveAssignmentIsTrainer_StaffManagementEndpointReturns403_EvenWithGymAdminElsewhere()
    {
        var seed = await SeedAsync();

        // GET /api/package-assignments -> StaffManagement politikası.
        var asTrainer = await ClientFor(seed.MultiRoleToken, seed.TrainerA1AssignmentId).GetAsync("/api/package-assignments");
        var asGymAdminB = await ClientFor(seed.MultiRoleToken, seed.GymAdminBAssignmentId).GetAsync("/api/package-assignments");

        Assert.Equal(HttpStatusCode.Forbidden, asTrainer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asGymAdminB.StatusCode);
    }

    [Fact]
    public async Task Policy_ActiveAssignmentIsBranchManager_GymAdminOnlyEndpointReturns403()
    {
        var seed = await SeedAsync();

        // POST /api/branches -> GymAdminOrSuperAdmin politikası. Kullanıcının B
        // firmasında GymAdmin ataması var ama aktif atama A2'deki BranchManager.
        var response = await ClientFor(seed.MultiRoleToken, seed.ManagerA2AssignmentId)
            .PostAsJsonAsync("/api/branches", new { companyId = seed.CompanyAId, name = "Yeni", address = "..." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_ReturnsEachAssignmentsIdAndBranchName()
    {
        var seed = await SeedAsync();

        var response = await ClientFor(seed.MultiRoleToken).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        var assignments = me.GetProperty("assignments").EnumerateArray().ToList();
        var trainer = assignments.Single(a => a.GetProperty("id").GetInt32() == seed.TrainerA1AssignmentId);
        Assert.Equal("A1", trainer.GetProperty("branchName").GetString());
        var gymAdmin = assignments.Single(a => a.GetProperty("id").GetInt32() == seed.GymAdminBAssignmentId);
        Assert.Equal(JsonValueKind.Null, gymAdmin.GetProperty("branchName").ValueKind);
        // Pasif atama listelenmez.
        Assert.DoesNotContain(assignments, a => a.GetProperty("id").GetInt32() == seed.InactiveAssignmentId);
    }
}
