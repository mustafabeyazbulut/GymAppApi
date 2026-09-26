using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Time;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.IntegrationTests;

// Adım 6 - Sistem Sahibi platform raporları. Sayımlar kesin olsun diye her
// sayım testi kendi (boş) veritabanıyla çalışır.
public class PlatformReportsTests
{
    private static int _phoneSeq;
    private static string UniquePhone() => $"+90555260{Interlocked.Increment(ref _phoneSeq):D4}";

    private sealed record Seed(
        int CompanyAId, int CompanyBId, int BranchA1Id, int BranchA2Id,
        string SuperAdminToken, string SuperAdminWithGymRoleToken, int SuperAdminGymAssignmentId, string GymAdminToken);

    // Firma A (aktif): şube A1 (aktif), A2 (kapalı). Firma B (pasif): şube B1.
    // Üyeler: m1 (A1 geçerli), m2 (A1 + B1 geçerli), m3 (A1 süresi dolmuş, 20 gün önce satılmış).
    // Personel: GymAdmin A, BM A1, Trainer A1, GymAdmin B, Trainer B1.
    // Ödemeler: m1 100, m2-A 50, m2-B 70 (bugün); 999 (30 gün önce, dönem dışı).
    private static async Task<Seed> SeedAsync(CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var now = DateTime.UtcNow;

        var companyA = new Company { Name = "Firma A", IsActive = true };
        var companyB = new Company { Name = "Firma B", IsActive = false };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();
        var a1 = new Branch { CompanyId = companyA.Id, Name = "A1", Address = "..." };
        var a2 = new Branch { CompanyId = companyA.Id, Name = "A2", Address = "...", IsActive = false };
        var b1 = new Branch { CompanyId = companyB.Id, Name = "B1", Address = "..." };
        db.Branches.AddRange(a1, a2, b1);
        var packageA = new Package { CompanyId = companyA.Id, BranchId = a1.Id, Name = "PA", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        var packageB = new Package { CompanyId = companyB.Id, BranchId = b1.Id, Name = "PB", Type = PackageType.Duration, DurationDays = 30, Price = 100m };
        db.Packages.AddRange(packageA, packageB);

        User NewUser(string name) => new() { FullName = name, Phone = UniquePhone(), PasswordHash = "x" };
        var superAdmin = NewUser("SA");
        var superAdminWithGymRole = NewUser("SA + Gym");
        var gymAdminA = NewUser("GA A");
        var bmA1 = NewUser("BM A1");
        var trainerA1 = NewUser("T A1");
        var gymAdminB = NewUser("GA B");
        var trainerB1 = NewUser("T B1");
        var m1 = NewUser("m1");
        var m2 = NewUser("m2");
        var m3 = NewUser("m3");
        var oldUser = NewUser("eski");
        var threeDaysAgoUser = NewUser("3 gün önce");
        db.Users.AddRange(superAdmin, superAdminWithGymRole, gymAdminA, bmA1, trainerA1, gymAdminB, trainerB1, m1, m2, m3, oldUser, threeDaysAgoUser);
        await db.SaveChangesAsync();

        var saGymAssignment = new Assignment { UserId = superAdminWithGymRole.Id, CompanyId = companyA.Id, BranchId = a1.Id, Role = AssignmentRole.Trainer };
        db.Assignments.AddRange(
            new Assignment { UserId = superAdmin.Id, Role = AssignmentRole.SuperAdmin },
            new Assignment { UserId = superAdminWithGymRole.Id, Role = AssignmentRole.SuperAdmin },
            saGymAssignment,
            new Assignment { UserId = gymAdminA.Id, CompanyId = companyA.Id, Role = AssignmentRole.GymAdmin },
            new Assignment { UserId = bmA1.Id, CompanyId = companyA.Id, BranchId = a1.Id, Role = AssignmentRole.BranchManager },
            new Assignment { UserId = trainerA1.Id, CompanyId = companyA.Id, BranchId = a1.Id, Role = AssignmentRole.Trainer },
            new Assignment { UserId = gymAdminB.Id, CompanyId = companyB.Id, Role = AssignmentRole.GymAdmin },
            new Assignment { UserId = trainerB1.Id, CompanyId = companyB.Id, BranchId = b1.Id, Role = AssignmentRole.Trainer },
            // Pasif atama sayılmaz.
            new Assignment { UserId = m3.Id, CompanyId = companyA.Id, BranchId = a1.Id, Role = AssignmentRole.Trainer, IsActive = false });

        PackageAssignment Membership(User member, Package package, bool valid) => new()
        {
            PackageId = package.Id, MemberUserId = member.Id, CompanyId = package.CompanyId, BranchId = package.BranchId, AssignedByUserId = gymAdminA.Id,
            StartDate = now.AddDays(-5), EndDate = valid ? now.AddDays(25) : now.AddDays(-1),
        };
        var m1A = Membership(m1, packageA, valid: true);
        var m2A = Membership(m2, packageA, valid: true);
        var m2B = Membership(m2, packageB, valid: true);
        var m3A = Membership(m3, packageA, valid: false);
        db.PackageAssignments.AddRange(m1A, m2A, m2B, m3A);
        await db.SaveChangesAsync();

        PackageAssignmentPayment Payment(PackageAssignment pa, decimal amount, DateTime paidAt) => new()
        {
            PackageAssignmentId = pa.Id, CompanyId = pa.CompanyId, Amount = amount, Method = PaymentMethod.Cash, PaidAt = paidAt, RecordedByUserId = gymAdminA.Id,
        };
        db.PackageAssignmentPayments.AddRange(
            Payment(m1A, 100m, now.AddMinutes(-5)),
            Payment(m2A, 50m, now.AddMinutes(-5)),
            Payment(m2B, 70m, now.AddMinutes(-5)),
            Payment(m3A, 999m, now.AddDays(-30)));
        await db.SaveChangesAsync();

        // CreatedAt SaveChanges'te "şimdi" damgalanıyor; geçmişe taşımak için
        // sonradan güncelleniyor (güncelleme sadece UpdatedAt'e dokunur).
        oldUser.CreatedAt = now.AddDays(-10);
        threeDaysAgoUser.CreatedAt = now.AddDays(-3);
        m3A.CreatedAt = now.AddDays(-20);
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return new Seed(companyA.Id, companyB.Id, a1.Id, a2.Id, Token(superAdmin), Token(superAdminWithGymRole), saGymAssignment.Id, Token(gymAdminA));
    }

    private static HttpClient ClientFor(CustomWebApplicationFactory factory, string token, int? activeAssignmentId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (activeAssignmentId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Active-Assignment-Id", activeAssignmentId.ToString());
        }
        return client;
    }

    [Fact]
    public async Task Summary_AsSuperAdmin_ReturnsPlatformTotalsAndPerCompanyBreakdown()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);

        var response = await ClientFor(factory, seed.SuperAdminToken).GetAsync("/api/platform-reports/summary?days=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var allUsers = await db.Users.IgnoreQueryFilters().Select(u => u.CreatedAt).ToListAsync();
        var today = TurkeyCalendar.LocalDate(DateTime.UtcNow);
        var fromDate = today.AddDays(-6);

        Assert.Equal(allUsers.Count, body.GetProperty("totalUsers").GetInt32());
        Assert.Equal(allUsers.Count(c => TurkeyCalendar.LocalDate(c) >= fromDate), body.GetProperty("newUsersInPeriod").GetInt32());
        Assert.Equal(2, body.GetProperty("companyCount").GetInt32());
        Assert.Equal(1, body.GetProperty("activeCompanyCount").GetInt32());
        Assert.Equal(2, body.GetProperty("branchCount").GetInt32());
        Assert.Equal(2, body.GetProperty("activeMemberCount").GetInt32());
        Assert.Equal(3, body.GetProperty("trainerCount").GetInt32());
        Assert.Equal(3, body.GetProperty("staffCount").GetInt32());
        Assert.Equal(3, body.GetProperty("packageSalesInPeriod").GetInt32());
        Assert.Equal(220m, body.GetProperty("revenueInPeriod").GetDecimal());
        Assert.Equal("TRY", body.GetProperty("currency").GetString());

        // Boş günler 0 ile dolu, eskiden yeniye 7 gün.
        var growth = body.GetProperty("userGrowth").EnumerateArray().ToList();
        Assert.Equal(7, growth.Count);
        Assert.Equal(fromDate.ToString("yyyy-MM-dd"), growth[0].GetProperty("date").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), growth[^1].GetProperty("date").GetString());
        for (var i = 0; i < 7; i++)
        {
            var date = fromDate.AddDays(i);
            Assert.Equal(allUsers.Count(c => TurkeyCalendar.LocalDate(c) == date), growth[i].GetProperty("newUsers").GetInt32());
        }
        Assert.Contains(growth, g => g.GetProperty("newUsers").GetInt32() == 0);

        var companies = body.GetProperty("companies").EnumerateArray().ToList();
        var a = companies.Single(c => c.GetProperty("companyId").GetInt32() == seed.CompanyAId);
        Assert.Equal("Firma A", a.GetProperty("companyName").GetString());
        Assert.True(a.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, a.GetProperty("branchCount").GetInt32());
        Assert.Equal(2, a.GetProperty("activeMemberCount").GetInt32());
        Assert.Equal(2, a.GetProperty("trainerCount").GetInt32());
        Assert.Equal(2, a.GetProperty("staffCount").GetInt32());
        Assert.Equal(2, a.GetProperty("packageSalesInPeriod").GetInt32());
        Assert.Equal(150m, a.GetProperty("revenueInPeriod").GetDecimal());
        var b = companies.Single(c => c.GetProperty("companyId").GetInt32() == seed.CompanyBId);
        Assert.False(b.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, b.GetProperty("activeMemberCount").GetInt32());
        Assert.Equal(1, b.GetProperty("trainerCount").GetInt32());
        Assert.Equal(1, b.GetProperty("staffCount").GetInt32());
        Assert.Equal(1, b.GetProperty("packageSalesInPeriod").GetInt32());
        Assert.Equal(70m, b.GetProperty("revenueInPeriod").GetDecimal());
    }

    [Fact]
    public async Task CompanyBranches_AsSuperAdmin_ReturnsPerBranchBreakdown()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);

        var response = await ClientFor(factory, seed.SuperAdminToken).GetAsync($"/api/platform-reports/companies/{seed.CompanyAId}/branches?days=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branches = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(2, branches.Count);
        var a1 = branches.Single(b => b.GetProperty("branchId").GetInt32() == seed.BranchA1Id);
        Assert.Equal("A1", a1.GetProperty("branchName").GetString());
        Assert.True(a1.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, a1.GetProperty("activeMemberCount").GetInt32());
        Assert.Equal(2, a1.GetProperty("trainerCount").GetInt32());
        Assert.Equal(1, a1.GetProperty("staffCount").GetInt32());
        // 30 günlük dönemde m3'ün 20 gün önceki satışı da var; 30 gün önceki ödeme sınırda dışarıda.
        Assert.Equal(3, a1.GetProperty("packageSalesInPeriod").GetInt32());
        Assert.Equal(150m, a1.GetProperty("revenueInPeriod").GetDecimal());
        var a2 = branches.Single(b => b.GetProperty("branchId").GetInt32() == seed.BranchA2Id);
        Assert.False(a2.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, a2.GetProperty("activeMemberCount").GetInt32());
        Assert.Equal(0m, a2.GetProperty("revenueInPeriod").GetDecimal());
    }

    [Fact]
    public async Task Access_AndValidationRules()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var superAdmin = ClientFor(factory, seed.SuperAdminToken);

        // SuperAdmin olmayan -> 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(factory, seed.GymAdminToken).GetAsync("/api/platform-reports/summary?days=7")).StatusCode);
        // Personel görevinde (header'lı) SuperAdmin -> 403.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientFor(factory, seed.SuperAdminWithGymRoleToken, seed.SuperAdminGymAssignmentId).GetAsync("/api/platform-reports/summary?days=7")).StatusCode);
        // Header'sız aynı kullanıcı -> 200.
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(factory, seed.SuperAdminWithGymRoleToken).GetAsync("/api/platform-reports/summary?days=7")).StatusCode);

        // Geçersiz dönem -> 400.
        var invalid = await superAdmin.GetAsync("/api/platform-reports/summary?days=10");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("InvalidReportPeriod", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.GetAsync($"/api/platform-reports/companies/{seed.CompanyAId}/branches?days=0")).StatusCode);

        // Olmayan firma -> 404.
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync("/api/platform-reports/companies/999999/branches?days=7")).StatusCode);

        // Desteklenen tüm dönemler.
        foreach (var days in new[] { 7, 30, 90, 365 })
        {
            var ok = await superAdmin.GetFromJsonAsync<JsonElement>($"/api/platform-reports/summary?days={days}");
            Assert.Equal(days, ok.GetProperty("userGrowth").GetArrayLength());
        }
    }
}
