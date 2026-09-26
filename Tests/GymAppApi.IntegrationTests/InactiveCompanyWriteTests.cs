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

    private sealed record MixedSeed(
        int PackageAssignmentId, int FrozenPackageAssignmentId, int ReservationId, string ReservationCode, int ClassSessionId, int EnrollmentId,
        int TrainerId, int TrainerAssignmentId, string MemberToken, string TrainerToken, string GymAdminToken);

    // Sade [Authorize] ile korunan karma uçlar için: pasif firmada personel ve
    // üye YENİ işlem yapamaz (senaryo §7: firma pasifken geçmiş veri durur).
    private async Task<MixedSeed> SeedMixedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var now = DateTime.UtcNow;

        var company = new Company { Name = "Kapalı Firma", IsActive = false };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "PT", Type = PackageType.SessionBased, SessionCount = 10, Price = 1m };
        db.Packages.Add(package);
        var admin = new User { FullName = "GA", Phone = UniquePhone(), PasswordHash = "x" };
        var trainer = new User { FullName = "T", Phone = UniquePhone(), PasswordHash = "x" };
        var member = new User { FullName = "Üye", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(admin, trainer, member);
        await db.SaveChangesAsync();
        var trainerAssignment = new Assignment { UserId = trainer.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.Trainer };
        db.Assignments.AddRange(new Assignment { UserId = admin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin }, trainerAssignment);

        PackageAssignment Membership(PackageAssignmentStatus status) => new()
        {
            PackageId = package.Id, MemberUserId = member.Id, CompanyId = company.Id, BranchId = branch.Id, AssignedByUserId = admin.Id,
            StartDate = now.AddDays(-5), EndDate = now.AddDays(25), RemainingSessions = 5, Status = status,
            FrozenAt = status == PackageAssignmentStatus.Frozen ? now.AddDays(-1) : null,
        };
        var membership = Membership(PackageAssignmentStatus.Active);
        var frozen = Membership(PackageAssignmentStatus.Frozen);
        db.PackageAssignments.AddRange(membership, frozen);
        var session = new ClassSession
        {
            CompanyId = company.Id, BranchId = branch.Id, TrainerUserId = trainer.Id, Category = ClassSessionCategory.GroupClass, Name = "Yoga",
            Date = DateOnly.FromDateTime(now.AddDays(3)), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), Capacity = 5, CreatedByUserId = admin.Id,
        };
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        var code = Guid.NewGuid().ToString("N")[..8];
        var reservation = new Reservation
        {
            PackageAssignmentId = membership.Id, MemberUserId = member.Id, TrainerId = trainer.Id, CompanyId = company.Id, BranchId = branch.Id,
            ScheduledAt = now.AddHours(-1), CreatedByUserId = member.Id, QrCode = code,
        };
        var enrollment = new ClassEnrollment
        {
            ClassSessionId = session.Id, PackageAssignmentId = membership.Id, MemberUserId = member.Id, CompanyId = company.Id, BranchId = branch.Id,
            ReservedAt = now,
        };
        db.Reservations.Add(reservation);
        db.ClassEnrollments.Add(enrollment);
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token(User u) => jwt.GenerateAccessToken(new AccessTokenClaims(u.Id, u.FullName, u.Email, u.Phone)).Token;
        return new MixedSeed(membership.Id, frozen.Id, reservation.Id, code, session.Id, enrollment.Id, trainer.Id, trainerAssignment.Id,
            Token(member), Token(trainer), Token(admin));
    }

    [Fact]
    public async Task InAnInactiveCompany_MixedEndpointWrites_ByStaffAndMembers_Return403CompanyInactive()
    {
        var seed = await SeedMixedAsync();
        var member = ClientFor(seed.MemberToken);
        var trainer = ClientFor(seed.TrainerToken);
        var admin = ClientFor(seed.GymAdminToken);

        var progressNote = new MultipartFormDataContent
        {
            { new StringContent("50"), "techniqueScore" },
            { new StringContent("50"), "conditionScore" },
        };
        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["member freeze"] = await member.PostAsync($"/api/package-assignments/{seed.PackageAssignmentId}/freeze", null),
            ["member unfreeze"] = await member.PostAsync($"/api/package-assignments/{seed.FrozenPackageAssignmentId}/unfreeze", null),
            ["member create reservation"] = await member.PostAsJsonAsync("/api/reservations",
                new { packageAssignmentId = seed.PackageAssignmentId, trainerId = seed.TrainerId, scheduledAt = DateTime.UtcNow.AddDays(2) }),
            ["member cancel reservation"] = await member.PostAsync($"/api/reservations/{seed.ReservationId}/cancel", null),
            ["trainer no-show"] = await trainer.PostAsync($"/api/reservations/{seed.ReservationId}/no-show", null),
            ["trainer check-in"] = await trainer.PostAsync($"/api/reservations/{seed.ReservationId}/check-in", null),
            ["trainer check-in by code"] = await trainer.PostAsJsonAsync("/api/reservations/checkin-by-code", new { code = seed.ReservationCode }),
            ["trainer progress note"] = await trainer.PostAsync($"/api/package-assignments/{seed.PackageAssignmentId}/progress-notes", progressNote),
            ["admin remove assignment"] = await admin.DeleteAsync($"/api/assignments/{seed.TrainerAssignmentId}"),
            ["member enroll"] = await member.PostAsJsonAsync($"/api/class-sessions/{seed.ClassSessionId}/enroll", new { packageAssignmentId = seed.PackageAssignmentId }),
            ["member cancel enrollment"] = await member.PostAsync($"/api/class-enrollments/{seed.EnrollmentId}/cancel", null),
        };

        foreach (var (action, response) in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{action} -> {(int)response.StatusCode}, 403 bekleniyordu.");
            Assert.Equal("CompanyInactive", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Code").GetString());
        }
    }

    [Fact]
    public async Task InAnInactiveCompany_Reads_StillWork()
    {
        var seed = await SeedMixedAsync();

        Assert.Equal(HttpStatusCode.OK, (await ClientFor(seed.MemberToken).GetAsync($"/api/package-assignments/{seed.PackageAssignmentId}/reservations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(seed.MemberToken).GetAsync("/api/class-enrollments/mine")).StatusCode);
    }

    [Fact]
    public async Task GymAdminOfAnActiveCompany_StaffWrites_AreNotBlocked()
    {
        var seed = await SeedAsync(companyActive: true);

        var response = await ClientFor(seed.GymAdminToken).PatchAsJsonAsync($"/api/packages/{seed.PackageId}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
