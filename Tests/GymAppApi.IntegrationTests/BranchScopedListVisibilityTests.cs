using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

// Senaryo §10.8: Şube Yöneticisi ve Antrenör sadece atandıkları şubenin
// verisini görür; hiçbir liste başka şubenin veya başka firmanın verisini
// döndürmez. Bu testler liste/detay uç noktalarını gerçek HTTP pipeline'ı
// (TenantContextMiddleware + global query filter + handler) üzerinden
// uçtan uca doğrular.
public class BranchScopedListVisibilityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BranchScopedListVisibilityTests(CustomWebApplicationFactory factory) => _factory = factory;

    // Bu factory'nin DB'si sınıftaki tüm testlerce paylaşılıyor - her seed
    // kendi firmalarını ve rastgele telefonlarını oluşturur.
    private static string UniquePhone() => $"+9055507{Random.Shared.Next(10000, 99999)}";

    private sealed record Seed(
        int BranchA1Id, int BranchA2Id, int BranchB1Id,
        int PackageA1Id, int PackageA2Id, int PackageACompanyWideId, int PackageB1Id,
        int AssignmentA1Id, int AssignmentA2Id,
        int SessionA1Id, int SessionA2Id, int SessionB1Id,
        int ContentA1Id, int ContentA2Id, int ContentACompanyWideId, int ContentB1Id,
        string GymAdminAToken, string BranchManagerA1Token, string TrainerA1Token,
        string MemberWithB1PackageToken, string MemberWithoutPackageToken, string MemberWithExpiredB1PackageToken);

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

        var gymAdminA = new User { FullName = "Gym Admin A", Phone = UniquePhone(), PasswordHash = "x" };
        var branchManagerA1 = new User { FullName = "Şube Yöneticisi A1", Phone = UniquePhone(), PasswordHash = "x" };
        var trainerA1 = new User { FullName = "Antrenör A1", Phone = UniquePhone(), PasswordHash = "x" };
        var memberA1 = new User { FullName = "Üye A1", Phone = UniquePhone(), PasswordHash = "x" };
        var memberA2 = new User { FullName = "Üye A2", Phone = UniquePhone(), PasswordHash = "x" };
        var memberWithB1Package = new User { FullName = "Üye B1", Phone = UniquePhone(), PasswordHash = "x" };
        var memberWithoutPackage = new User { FullName = "Paketsiz Üye", Phone = UniquePhone(), PasswordHash = "x" };
        var memberWithExpiredB1Package = new User { FullName = "Süresi Dolmuş Üye", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(gymAdminA, branchManagerA1, trainerA1, memberA1, memberA2, memberWithB1Package, memberWithoutPackage, memberWithExpiredB1Package);
        await db.SaveChangesAsync();

        db.Assignments.AddRange(
            new Assignment { UserId = gymAdminA.Id, CompanyId = companyA.Id, Role = AssignmentRole.GymAdmin, IsActive = true },
            new Assignment { UserId = branchManagerA1.Id, CompanyId = companyA.Id, BranchId = branchA1.Id, Role = AssignmentRole.BranchManager, IsActive = true },
            new Assignment { UserId = trainerA1.Id, CompanyId = companyA.Id, BranchId = branchA1.Id, Role = AssignmentRole.Trainer, IsActive = true });

        var packageA1 = new Package { CompanyId = companyA.Id, BranchId = branchA1.Id, Name = "A1 Paketi", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        var packageA2 = new Package { CompanyId = companyA.Id, BranchId = branchA2.Id, Name = "A2 Paketi", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        var packageACompanyWide = new Package { CompanyId = companyA.Id, BranchId = null, Name = "A Firma Geneli", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        var packageB1 = new Package { CompanyId = companyB.Id, BranchId = branchB1.Id, Name = "B1 Paketi", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        db.Packages.AddRange(packageA1, packageA2, packageACompanyWide, packageB1);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var assignmentA1 = new PackageAssignment { PackageId = packageA1.Id, MemberUserId = memberA1.Id, CompanyId = companyA.Id, BranchId = branchA1.Id, AssignedByUserId = gymAdminA.Id, StartDate = now, EndDate = now.AddDays(30) };
        var assignmentA2 = new PackageAssignment { PackageId = packageA2.Id, MemberUserId = memberA2.Id, CompanyId = companyA.Id, BranchId = branchA2.Id, AssignedByUserId = gymAdminA.Id, StartDate = now, EndDate = now.AddDays(30) };
        var assignmentB1 = new PackageAssignment { PackageId = packageB1.Id, MemberUserId = memberWithB1Package.Id, CompanyId = companyB.Id, BranchId = branchB1.Id, AssignedByUserId = gymAdminA.Id, StartDate = now, EndDate = now.AddDays(30) };
        var expiredB1 = new PackageAssignment { PackageId = packageB1.Id, MemberUserId = memberWithExpiredB1Package.Id, CompanyId = companyB.Id, BranchId = branchB1.Id, AssignedByUserId = gymAdminA.Id, StartDate = now.AddDays(-60), EndDate = now.AddDays(-30) };
        db.PackageAssignments.AddRange(assignmentA1, assignmentA2, assignmentB1, expiredB1);

        ClassSession Session(Company company, Branch branch) => new()
        {
            CompanyId = company.Id, BranchId = branch.Id, TrainerUserId = trainerA1.Id, Category = ClassSessionCategory.GroupClass,
            Name = $"Ders {branch.Name}", Date = DateOnly.FromDateTime(now.AddDays(3)), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0),
            Capacity = 10, CancellationCutoffHours = 2, CreatedByUserId = gymAdminA.Id,
        };
        var sessionA1 = Session(companyA, branchA1);
        var sessionA2 = Session(companyA, branchA2);
        var sessionB1 = Session(companyB, branchB1);
        db.ClassSessions.AddRange(sessionA1, sessionA2, sessionB1);

        var media = new MediaFile { StoragePath = "test/video.mp4", ContentType = "video/mp4", SizeBytes = 1, UploadedByUserId = gymAdminA.Id };
        db.MediaFiles.Add(media);
        await db.SaveChangesAsync();

        ContentItem Content(Company company, Branch? branch, string title) => new()
        {
            CompanyId = company.Id, BranchId = branch?.Id, Title = title, RequiredAccessTier = PackageAccessTier.Standard,
            MediaFileId = media.Id, CreatedByUserId = gymAdminA.Id,
        };
        var contentA1 = Content(companyA, branchA1, "A1 İçerik");
        var contentA2 = Content(companyA, branchA2, "A2 İçerik");
        var contentACompanyWide = Content(companyA, null, "A Firma Geneli İçerik");
        var contentB1 = Content(companyB, branchB1, "B1 İçerik");
        db.ContentItems.AddRange(contentA1, contentA2, contentACompanyWide, contentB1);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwtService.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;

        return new Seed(
            branchA1.Id, branchA2.Id, branchB1.Id,
            packageA1.Id, packageA2.Id, packageACompanyWide.Id, packageB1.Id,
            assignmentA1.Id, assignmentA2.Id,
            sessionA1.Id, sessionA2.Id, sessionB1.Id,
            contentA1.Id, contentA2.Id, contentACompanyWide.Id, contentB1.Id,
            Token(gymAdminA), Token(branchManagerA1), Token(trainerA1),
            Token(memberWithB1Package), Token(memberWithoutPackage), Token(memberWithExpiredB1Package));
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int[]> GetIdsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        return items.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).OrderBy(id => id).ToArray();
    }

    private static int[] Sorted(params int[] ids) => ids.OrderBy(id => id).ToArray();

    // --- GET /api/class-sessions ---

    [Fact]
    public async Task ClassSessions_AsMemberWithoutPackage_ReturnsEmptyList()
    {
        var seed = await SeedAsync();

        var ids = await GetIdsAsync(ClientFor(seed.MemberWithoutPackageToken), "/api/class-sessions");

        Assert.Empty(ids);
    }

    [Fact]
    public async Task ClassSessions_AsMemberWithExpiredPackage_ReturnsEmptyList()
    {
        var seed = await SeedAsync();

        var ids = await GetIdsAsync(ClientFor(seed.MemberWithExpiredB1PackageToken), "/api/class-sessions");

        Assert.Empty(ids);
    }

    [Fact]
    public async Task ClassSessions_AsMemberWithValidPackage_ReturnsOnlyThatBranchsSessions()
    {
        var seed = await SeedAsync();

        var ids = await GetIdsAsync(ClientFor(seed.MemberWithB1PackageToken), "/api/class-sessions");

        Assert.Equal(Sorted(seed.SessionB1Id), ids);
    }

    [Fact]
    public async Task ClassSessions_AsGymAdmin_ReturnsOwnCompanysSessionsOnly()
    {
        var seed = await SeedAsync();

        var ids = await GetIdsAsync(ClientFor(seed.GymAdminAToken), "/api/class-sessions");

        Assert.Equal(Sorted(seed.SessionA1Id, seed.SessionA2Id), ids);
    }

    [Fact]
    public async Task ClassSessions_AsBranchManager_ReturnsOwnBranchsSessionsOnly()
    {
        var seed = await SeedAsync();

        var ids = await GetIdsAsync(ClientFor(seed.BranchManagerA1Token), "/api/class-sessions");

        Assert.Equal(Sorted(seed.SessionA1Id), ids);
    }

    [Fact]
    public async Task ClassSessions_AsTrainer_ReturnsOwnBranchsSessionsOnly()
    {
        var seed = await SeedAsync();

        var ids = await GetIdsAsync(ClientFor(seed.TrainerA1Token), "/api/class-sessions");

        Assert.Equal(Sorted(seed.SessionA1Id), ids);
    }
}
