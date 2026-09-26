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

// Canlı test bulgusu: pasif firmadaki Gym Admin'in yazma işlemleri 404
// "Firma bulunamadı" dönüyordu. Aktif bağlamın firması pasifse personel
// yazma uçları 403 CompanyInactive döner; kişisel uçlar etkilenmez.
public class InactiveCompanyWriteTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InactiveCompanyWriteTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055525{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(int CompanyId, int BranchId, int PackageId, string GymAdminToken);

    private async Task<Seed> SeedAsync(bool companyActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = companyActive };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "Aylık", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        db.Packages.Add(package);
        var admin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = admin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(new AccessTokenClaims(admin.Id, admin.FullName, admin.Email, admin.Phone)).Token;
        return new Seed(company.Id, branch.Id, package.Id, token);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GymAdminOfAnInactiveCompany_StaffWrites_Return403CompanyInactive()
    {
        var seed = await SeedAsync(companyActive: false);
        var client = ClientFor(seed.GymAdminToken);

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["POST /api/branches"] = await client.PostAsJsonAsync("/api/branches", new { companyId = seed.CompanyId, name = "Yeni", address = "..." }),
            ["POST /api/packages"] = await client.PostAsJsonAsync("/api/packages",
                new { companyId = seed.CompanyId, branchId = seed.BranchId, name = "P", type = "Duration", durationDays = 30, price = 1 }),
            ["PATCH /api/packages/{id}/active"] = await client.PatchAsJsonAsync($"/api/packages/{seed.PackageId}/active", new { isActive = false }),
        };

        foreach (var (endpoint, response) in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{endpoint} -> {(int)response.StatusCode}, 403 bekleniyordu.");
            Assert.Equal("CompanyInactive", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());
        }
    }

    [Fact]
    public async Task GymAdminOfAnInactiveCompany_PersonalWrites_StillWork()
    {
        var seed = await SeedAsync(companyActive: false);
        var client = ClientFor(seed.GymAdminToken);

        var language = await client.PatchAsJsonAsync("/api/auth/me/language", new { language = "en" });
        var personalLog = await client.PostAsJsonAsync("/api/personal-logs",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), kind = "Workout", title = "Koşu" });

        Assert.Equal(HttpStatusCode.NoContent, language.StatusCode);
        Assert.Equal(HttpStatusCode.Created, personalLog.StatusCode);
    }

    [Fact]
    public async Task GymAdminOfAnActiveCompany_StaffWrites_AreNotBlocked()
    {
        var seed = await SeedAsync(companyActive: true);

        var response = await ClientFor(seed.GymAdminToken).PatchAsJsonAsync($"/api/packages/{seed.PackageId}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
