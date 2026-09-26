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

// Senaryo §4.5: kapı/bölge erişim kurallarını Gym Admin tanımlar. Şube
// Yöneticisi kendi şubesinin bölge/kapı/kurallarını görüntüleyebilir ama
// yazamaz (oluşturma/silme).
public class DoorAccessAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DoorAccessAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055513{Random.Shared.Next(10000, 99999)}";

    private async Task<(int BranchId, int ZoneId, string GymAdminToken, string BranchManagerToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Firma", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var zone = new Zone { CompanyId = company.Id, BranchId = branch.Id, Name = "Ana Salon" };
        db.Zones.Add(zone);

        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var manager = new User { FullName = "Şube Yöneticisi", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(gymAdmin, manager);
        await db.SaveChangesAsync();
        db.Assignments.AddRange(
            new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true },
            new Assignment { UserId = manager.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.BranchManager, IsActive = true });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return (branch.Id, zone.Id, Token(gymAdmin), Token(manager));
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task BranchManager_CannotWriteZonesDoorsOrRules_Returns403()
    {
        var (branchId, zoneId, _, managerToken) = await SeedAsync();
        var client = ClientFor(managerToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/zones", new { branchId, name = "Yeni Bölge" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/zones/{zoneId}/doors", new { name = "Kapı 1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/zones/{zoneId}")).StatusCode);
    }

    [Fact]
    public async Task BranchManager_CanStillViewOwnBranchsZones()
    {
        var (branchId, zoneId, _, managerToken) = await SeedAsync();

        var response = await ClientFor(managerToken).GetAsync($"/api/zones?branchId={branchId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zones = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(zones.EnumerateArray(), z => z.GetProperty("id").GetInt32() == zoneId);
    }

    [Fact]
    public async Task GymAdmin_CanCreateAZone()
    {
        var (branchId, _, gymAdminToken, _) = await SeedAsync();

        var response = await ClientFor(gymAdminToken).PostAsJsonAsync("/api/zones", new { branchId, name = "Havuz" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
