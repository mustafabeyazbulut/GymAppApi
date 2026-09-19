using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Commands.EnrollInClassSession;
using GymAppApi.Application.Features.ClassScheduling.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

// EnrollInClassSessionCommandHandler'daki kapasite kilidini (IUnitOfWork.
// GetForUpdateAsync -> Postgres "SELECT ... FOR UPDATE") GERÇEK bir Postgres'e
// karşı doğrular - unit testler (Moq ile) sadece iş kuralının mantığını test
// edebilir, gerçek bir race condition'ı (iki eşzamanlı transaction'ın aynı
// satırı kilitlemeye çalışması) sadece gerçek bir ilişkisel veritabanı
// motoruyla kanıtlayabiliriz. EF Core InMemory sağlayıcısı FromSqlRaw'ı ve
// gerçek transaction/satır kilidini desteklemediği için bu test, projenin
// diğer entegrasyon testlerinin kullandığı CustomWebApplicationFactory'yi
// (InMemory) DEĞİL, doğrudan gerçek bir Npgsql bağlantısı kullanır.
//
// Bu ortamda gerçek bir Postgres sunucusuna erişilemiyorsa test, bağlantı
// denemesinde erken çıkar (atlanmış sayılır) - bkz. görevin talimatındaki
// "Postgres'e bağlanamama durumunda atlanması kabul edilebilir" notu. Bağlantı
// dizesi GYMAPPAPI_TEST_POSTGRES_CONNECTION ortam değişkeninden okunur, yoksa
// standart yerel geliştirme varsayılanı kullanılır.
public class ClassSessionCapacityRaceConditionTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("GYMAPPAPI_TEST_POSTGRES_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=gymappapi_race_test;Username=postgres;Password=postgres";

    private static GymAppApiDbContext CreateContext(string databaseName)
    {
        var connectionStringWithDb = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName,
        }.ToString();

        var options = new DbContextOptionsBuilder<GymAppApiDbContext>()
            .UseNpgsql(connectionStringWithDb)
            .Options;
        return new GymAppApiDbContext(options, new AmbientTenantContext());
    }

    private static async Task<(bool connected, string databaseName)> TryPrepareDatabaseAsync()
    {
        var databaseName = $"gymappapi_race_test_{Guid.NewGuid():N}";
        try
        {
            await using var context = CreateContext(databaseName);
            // EnsureCreatedAsync: veritabanı yoksa oluşturur, sonra modelden
            // (migration history olmadan) doğrudan şemayı üretir - izole,
            // tek seferlik bir test veritabanı için migration'lardan daha
            // basit ve yeterli.
            await context.Database.EnsureCreatedAsync();
            return (true, databaseName);
        }
        catch
        {
            // Bu ortamda gerçek bir Postgres sunucusu yok/erişilemiyor.
            return (false, databaseName);
        }
    }

    // TransactionBehavior'ın MediatR pipeline'ında yaptığının birebir aynısı -
    // burada gerçek bir DI container/MediatR kurulumu olmadığı için handler'ı
    // aynı transaction+retry sarmalayıcısıyla elle çağırıyoruz.
    private static async Task<EnrollResult> EnrollAsync(string databaseName, int classSessionId, int packageAssignmentId, int memberUserId)
    {
        await using var context = CreateContext(databaseName);
        var unitOfWork = new UnitOfWork(context);
        var handler = new EnrollInClassSessionCommandHandler(unitOfWork);
        var command = new EnrollInClassSessionCommand
        {
            ClassSessionId = classSessionId,
            PackageAssignmentId = packageAssignmentId,
            RequestedByUserId = memberUserId,
        };

        try
        {
            var result = await unitOfWork.ExecuteWithRetryAsync(async () =>
            {
                await using var transaction = await unitOfWork.BeginTransactionAsync();
                try
                {
                    var response = await handler.Handle(command, CancellationToken.None);
                    await unitOfWork.CommitTransactionAsync();
                    return response;
                }
                catch
                {
                    await unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            });
            return new EnrollResult(true, result.Id, null);
        }
        catch (BaseException exception)
        {
            return new EnrollResult(false, null, exception);
        }
    }

    private record EnrollResult(bool Succeeded, int? EnrollmentId, BaseException? Exception);

    [Fact]
    public async Task ConcurrentEnroll_WhenCapacityIsOne_OnlyOneOfTwoSimultaneousRequestsSucceeds()
    {
        var (connected, databaseName) = await TryPrepareDatabaseAsync();
        if (!connected)
        {
            // Gerçek Postgres'e bağlanılamadı - görev talimatına göre bu kabul
            // edilebilir bir atlama, testin kendisi doğru yazılmış olmalı.
            return;
        }

        try
        {
            int classSessionId;
            int assignment1Id;
            int assignment2Id;

            await using (var seedContext = CreateContext(databaseName))
            {
                var company = new Company { Name = "Test Co", IsActive = true };
                seedContext.Companies.Add(company);
                await seedContext.SaveChangesAsync();

                var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
                seedContext.Branches.Add(branch);
                await seedContext.SaveChangesAsync();

                var trainer = new User { FullName = "Trainer", Phone = "+905550001111", PasswordHash = "x" };
                var member1 = new User { FullName = "Member One", Phone = "+905550002222", PasswordHash = "x" };
                var member2 = new User { FullName = "Member Two", Phone = "+905550003333", PasswordHash = "x" };
                seedContext.Users.AddRange(trainer, member1, member2);
                await seedContext.SaveChangesAsync();

                var package = new Package
                {
                    CompanyId = company.Id, BranchId = branch.Id, Name = "Grup Dersi Paketi",
                    Type = PackageType.SessionBased, SessionCount = 10, Price = 500m, IsActive = true,
                    Category = ClassSessionCategory.GroupClass,
                };
                seedContext.Packages.Add(package);
                await seedContext.SaveChangesAsync();

                var assignment1 = new PackageAssignment
                {
                    PackageId = package.Id, MemberUserId = member1.Id, CompanyId = company.Id, BranchId = branch.Id,
                    AssignedByUserId = trainer.Id, StartDate = DateTime.UtcNow, RemainingSessions = 10,
                    Status = PackageAssignmentStatus.Active,
                };
                var assignment2 = new PackageAssignment
                {
                    PackageId = package.Id, MemberUserId = member2.Id, CompanyId = company.Id, BranchId = branch.Id,
                    AssignedByUserId = trainer.Id, StartDate = DateTime.UtcNow, RemainingSessions = 10,
                    Status = PackageAssignmentStatus.Active,
                };
                seedContext.PackageAssignments.AddRange(assignment1, assignment2);
                await seedContext.SaveChangesAsync();

                var classSession = new ClassSession
                {
                    CompanyId = company.Id, BranchId = branch.Id, TrainerUserId = trainer.Id,
                    Category = ClassSessionCategory.GroupClass, Name = "Sabah Yogası",
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                    StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0),
                    // KRİTİK: kapasite 1 - iki eşzamanlı istekten sadece biri
                    // kazanabilmeli.
                    Capacity = 1,
                    CancellationCutoffHours = 2,
                    CreatedByUserId = trainer.Id,
                };
                seedContext.ClassSessions.Add(classSession);
                await seedContext.SaveChangesAsync();

                classSessionId = classSession.Id;
                assignment1Id = assignment1.Id;
                assignment2Id = assignment2.Id;
            }

            // RequestedByUserId, ilgili PackageAssignment'ın MemberUserId'siyle
            // eşleşmeli (handler kendi kaydı dışındakini reddeder) - eşzamanlı
            // istekleri ateşlemeden ÖNCE, sırayla öğreniyoruz.
            var member1UserId = await GetMemberUserIdAsync(databaseName, assignment1Id);
            var member2UserId = await GetMemberUserIdAsync(databaseName, assignment2Id);

            // İki eşzamanlı kayıt isteği - kapasite 1 olduğu için ikisi de
            // "kapasite dolu değil" okursa (satır kilidi olmasaydı olacağı
            // gibi) her ikisi de başarılı olur ve kapasite aşılır. Satır
            // kilidiyle sadece biri kazanmalı.
            var results = await Task.WhenAll(
                EnrollAsync(databaseName, classSessionId, assignment1Id, member1UserId),
                EnrollAsync(databaseName, classSessionId, assignment2Id, member2UserId));

            var succeeded = results.Count(r => r.Succeeded);
            var failed = results.Where(r => !r.Succeeded).ToList();

            Assert.Equal(1, succeeded);
            Assert.Single(failed);
            Assert.IsType<ClassSessionFullException>(failed[0].Exception);

            await using var verifyContext = CreateContext(databaseName);
            var activeEnrollmentCount = await verifyContext.ClassEnrollments
                .IgnoreQueryFilters()
                .CountAsync(e => e.ClassSessionId == classSessionId &&
                    (e.Status == ClassEnrollmentStatus.Reserved || e.Status == ClassEnrollmentStatus.Attended));
            Assert.Equal(1, activeEnrollmentCount);
        }
        finally
        {
            await using var cleanupContext = CreateContext(databaseName);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<int> GetMemberUserIdAsync(string databaseName, int packageAssignmentId)
    {
        await using var context = CreateContext(databaseName);
        var assignment = await context.PackageAssignments.IgnoreQueryFilters()
            .FirstAsync(a => a.Id == packageAssignmentId);
        return assignment.MemberUserId;
    }
}
