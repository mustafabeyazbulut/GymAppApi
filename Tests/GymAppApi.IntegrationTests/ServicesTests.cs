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

// Adım 7.1 - Hizmet (Service): şubeye ait, ders/randevu uygunluğunun temeli.
// Yazma: GymAdmin firma genelinde, BranchManager sadece kendi şubesinde.
// Okuma: o şubenin personeli ve o şubede geçerli paketi olan üye.
public class ServicesTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ServicesTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static int _seq;
    private static string UniquePhone() => $"+90555270{Interlocked.Increment(ref _seq):D4}";

    private sealed record Seed(
        int BranchA1Id, int BranchA2Id,
        string GymAdminToken, string BranchManagerA1Token, string TrainerA1Token,
        string MemberA1Token, string MemberWithoutPackageToken, string OtherCompanyAdminToken);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Firma", IsActive = true };
        var other = new Company { Name = "Diğer", IsActive = true };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();
        var a1 = new Branch { CompanyId = company.Id, Name = "A1", Address = "..." };
        var a2 = new Branch { CompanyId = company.Id, Name = "A2", Address = "..." };
        db.Branches.AddRange(a1, a2, new Branch { CompanyId = other.Id, Name = "O1", Address = "..." });
        var package = new Package { CompanyId = company.Id, Name = "Aylık", Type = PackageType.Duration, DurationDays = 30, Price = 1m };
        db.Packages.Add(package);

        User NewUser(string name) => new() { FullName = name, Phone = UniquePhone(), PasswordHash = "x" };
        var gymAdmin = NewUser("GA");
        var bm = NewUser("BM A1");
        var trainer = NewUser("T A1");
        var member = NewUser("Üye A1");
        var noPackage = NewUser("Paketsiz");
        var otherAdmin = NewUser("Diğer GA");
        db.Users.AddRange(gymAdmin, bm, trainer, member, noPackage, otherAdmin);
        await db.SaveChangesAsync();
        package.BranchId = a1.Id;
        db.Assignments.AddRange(
            new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin },
            new Assignment { UserId = bm.Id, CompanyId = company.Id, BranchId = a1.Id, Role = AssignmentRole.BranchManager },
            new Assignment { UserId = trainer.Id, CompanyId = company.Id, BranchId = a1.Id, Role = AssignmentRole.Trainer },
            new Assignment { UserId = otherAdmin.Id, CompanyId = other.Id, Role = AssignmentRole.GymAdmin });
        db.PackageAssignments.Add(new PackageAssignment
        {
            PackageId = package.Id, MemberUserId = member.Id, CompanyId = company.Id, BranchId = a1.Id, AssignedByUserId = gymAdmin.Id,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(29),
        });
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return new Seed(a1.Id, a2.Id, Token(gymAdmin), Token(bm), Token(trainer), Token(member), Token(noPackage), Token(otherAdmin));
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, int branchId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/branches/{branchId}/services", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<string>> NamesAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
    }

    [Fact]
    public async Task GymAdmin_CreatesServicesInAnyBranch_BranchManagerOnlyInOwn()
    {
        var seed = await SeedAsync();

        var created = await CreateAsync(ClientFor(seed.GymAdminToken), seed.BranchA2Id, "Pilates");
        Assert.Equal("Pilates", created.GetProperty("name").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(seed.BranchA2Id, created.GetProperty("branchId").GetInt32());

        await CreateAsync(ClientFor(seed.BranchManagerA1Token), seed.BranchA1Id, "Yoga");
        var bmOther = await ClientFor(seed.BranchManagerA1Token).PostAsJsonAsync($"/api/branches/{seed.BranchA2Id}/services", new { name = "Kickbox" });
        Assert.Equal(HttpStatusCode.Forbidden, bmOther.StatusCode);
    }

    [Fact]
    public async Task TrainersMembersAndOtherCompanies_CannotCreate()
    {
        var seed = await SeedAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientFor(seed.TrainerA1Token).PostAsJsonAsync($"/api/branches/{seed.BranchA1Id}/services", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientFor(seed.MemberA1Token).PostAsJsonAsync($"/api/branches/{seed.BranchA1Id}/services", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ClientFor(seed.OtherCompanyAdminToken).PostAsJsonAsync($"/api/branches/{seed.BranchA1Id}/services", new { name = "X" })).StatusCode);
    }

    [Fact]
    public async Task Name_IsRequired_AtMost60_AndUniqueWithinTheBranch()
    {
        var seed = await SeedAsync();
        var admin = ClientFor(seed.GymAdminToken);
        await CreateAsync(admin, seed.BranchA1Id, "Fonksiyonel");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/branches/{seed.BranchA1Id}/services", new { name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync($"/api/branches/{seed.BranchA1Id}/services", new { name = new string('x', 61) })).StatusCode);

        var duplicate = await admin.PostAsJsonAsync($"/api/branches/{seed.BranchA1Id}/services", new { name = "fonksiyonel " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("ServiceNameTaken", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());

        // Başka şubede aynı ad serbest.
        await CreateAsync(admin, seed.BranchA2Id, "Fonksiyonel");
    }

    [Fact]
    public async Task Rename_AndDeactivate_AndReactivate()
    {
        var seed = await SeedAsync();
        var admin = ClientFor(seed.GymAdminToken);
        var id = (await CreateAsync(admin, seed.BranchA1Id, "Spinning")).GetProperty("id").GetInt32();
        await CreateAsync(admin, seed.BranchA1Id, "Boks");

        var renamed = await admin.PatchAsJsonAsync($"/api/services/{id}", new { name = "Indoor Cycling" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Indoor Cycling", (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/services/{id}", new { name = "Boks" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/services/{id}/active", new { isActive = false })).StatusCode);
        Assert.DoesNotContain("Indoor Cycling", await NamesAsync(admin, $"/api/branches/{seed.BranchA1Id}/services"));
        Assert.Contains("Indoor Cycling", await NamesAsync(admin, $"/api/branches/{seed.BranchA1Id}/services?includeInactive=true"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/services/{id}/active", new { isActive = true })).StatusCode);
        Assert.Contains("Indoor Cycling", await NamesAsync(admin, $"/api/branches/{seed.BranchA1Id}/services"));
    }

    [Fact]
    public async Task BranchManager_CannotManageAnotherBranchsService()
    {
        var seed = await SeedAsync();
        var id = (await CreateAsync(ClientFor(seed.GymAdminToken), seed.BranchA2Id, "Crossfit")).GetProperty("id").GetInt32();
        var bm = ClientFor(seed.BranchManagerA1Token);

        Assert.Equal(HttpStatusCode.NotFound, (await bm.PatchAsJsonAsync($"/api/services/{id}", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bm.PatchAsJsonAsync($"/api/services/{id}/active", new { isActive = false })).StatusCode);
    }

    [Fact]
    public async Task Read_BranchStaffAndMembersWithAValidPackageThere_OthersForbidden()
    {
        var seed = await SeedAsync();
        await CreateAsync(ClientFor(seed.GymAdminToken), seed.BranchA1Id, "Zumba");
        var url = $"/api/branches/{seed.BranchA1Id}/services";

        Assert.Contains("Zumba", await NamesAsync(ClientFor(seed.TrainerA1Token), url));
        Assert.Contains("Zumba", await NamesAsync(ClientFor(seed.MemberA1Token), url));
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(seed.MemberWithoutPackageToken).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(seed.TrainerA1Token).GetAsync($"/api/branches/{seed.BranchA2Id}/services")).StatusCode);
    }
}
