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

// Senaryo §10.6: Sistem Sahibi gym'lerin günlük işlemlerini (paket, ders,
// check-in, ödeme, personel, şube, rapor vb.) yapamaz ve gym verisini
// göremez; sadece firma oluşturma/yönetme ve platform görünümü kalır.
public class SuperAdminGymRestrictionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SuperAdminGymRestrictionTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055512{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(int CompanyId, int BranchId, int PackageId, string SuperAdminToken, string CandidatePhone);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "Aylık", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        db.Packages.Add(package);
        db.ClassSessions.Add(new ClassSession
        {
            CompanyId = company.Id, BranchId = branch.Id, TrainerUserId = 1, Category = ClassSessionCategory.GroupClass, Name = "Yoga",
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), Capacity = 5, CreatedByUserId = 1,
        });

        var superAdmin = new User { FullName = "Sistem Sahibi", Phone = UniquePhone(), PasswordHash = "x" };
        var candidate = new User { FullName = "Aday", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(superAdmin, candidate);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;
        return new Seed(company.Id, branch.Id, package.Id, token, candidate.Phone);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task SuperAdmin_GymOperationEndpoints_Return403()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.SuperAdminToken);

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["POST /api/branches"] = await client.PostAsJsonAsync("/api/branches", new { companyId = seed.CompanyId, name = "Yeni", address = "..." }),
            ["PATCH /api/branches/{id}"] = await client.PatchAsJsonAsync($"/api/branches/{seed.BranchId}", new { name = "X", address = "..." }),
            ["POST /api/packages"] = await client.PostAsJsonAsync("/api/packages", new { companyId = seed.CompanyId, branchId = seed.BranchId, name = "P", type = "Duration", durationDays = 30, price = 1 }),
            ["GET /api/package-assignments"] = await client.GetAsync("/api/package-assignments"),
            ["POST /api/assignments/staff"] = await client.PostAsJsonAsync("/api/assignments/staff", new { phone = seed.CandidatePhone, role = "Trainer", branchId = seed.BranchId }),
            ["GET /api/assignments"] = await client.GetAsync("/api/assignments"),
            ["GET /api/reports/revenue"] = await client.GetAsync("/api/reports/revenue"),
            ["GET /api/analytics/summary"] = await client.GetAsync("/api/analytics/summary"),
            ["GET /api/zones"] = await client.GetAsync($"/api/zones?branchId={seed.BranchId}"),
        };

        foreach (var (endpoint, response) in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{endpoint} -> {(int)response.StatusCode}, 403 bekleniyordu.");
        }
    }

    [Fact]
    public async Task SuperAdmin_GymDataReadEndpoints_ReturnNothing_NoTenantBypass()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.SuperAdminToken);

        async Task<int> CountAsync(string url)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength();
        }

        Assert.Equal(0, await CountAsync("/api/branches"));
        Assert.Equal(0, await CountAsync("/api/packages"));
        Assert.Equal(0, await CountAsync("/api/class-sessions"));
        Assert.Equal(0, await CountAsync("/api/content-items"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/packages/{seed.PackageId}")).StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CompanyManagementEndpoints_StillWork()
    {
        var seed = await SeedAsync();
        var client = ClientFor(seed.SuperAdminToken);

        var companies = await client.GetAsync("/api/companies");
        Assert.Equal(HttpStatusCode.OK, companies.StatusCode);
        var list = await companies.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("id").GetInt32() == seed.CompanyId && c.GetProperty("branchCount").GetInt32() == 1);

        var detail = await client.GetAsync($"/api/companies/{seed.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var rename = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}", new { name = "Yeni Ad" });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        var invite = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId = seed.CompanyId, phone = seed.CandidatePhone });
        Assert.True(invite.IsSuccessStatusCode, $"InviteGymAdmin -> {(int)invite.StatusCode}");
    }
}
