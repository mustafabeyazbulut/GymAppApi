using GymAppApi.Application.Features.Packages.Commands.RecordGeneralCheckIn;
using GymAppApi.Application.Features.Reservations.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GymAppApi.IntegrationTests;

// İnceleme bulgusu [78]: check-in yollarında paket ataması kilitsiz okunup
// RemainingSessions -= 1 yapılıyordu; kalan son hakla aynı anda iki giriş
// hakkı iki kez kullanabiliyordu. Artık satır FOR UPDATE ile kilitli ve
// kontrol güncel değer üzerinden. Gerçek kilit sadece Postgres'te doğrulanır.
public class SessionRightsRaceConditionTests
{
    private readonly ITestOutputHelper _output;

    public SessionRightsRaceConditionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task TwoSimultaneousCheckIns_WithOneSessionLeft_ConsumeItOnlyOnce()
    {
        await using var database = await PostgresRaceTestDatabase.CreateAsync();
        if (!database.Connected)
        {
            _output.WriteLine("Postgres'e erişilemedi - yarış testi atlandı.");
            return;
        }

        int companyId;
        int adminId;
        int packageAssignmentId;
        await using (var seed = database.CreateContext())
        {
            var company = new Company { Name = "Firma", IsActive = true };
            seed.Companies.Add(company);
            await seed.SaveChangesAsync();
            var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
            seed.Branches.Add(branch);
            var admin = new User { FullName = "Admin", Phone = "+905550008001", PasswordHash = "x" };
            var member = new User { FullName = "Üye", Phone = "+905550008002", PasswordHash = "x" };
            seed.Users.AddRange(admin, member);
            await seed.SaveChangesAsync();
            var package = new Package { CompanyId = company.Id, BranchId = branch.Id, Name = "PT", Type = PackageType.SessionBased, SessionCount = 10, Price = 1m };
            seed.Packages.Add(package);
            seed.Assignments.Add(new Assignment { UserId = admin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
            await seed.SaveChangesAsync();
            var assignment = new PackageAssignment
            {
                PackageId = package.Id, MemberUserId = member.Id, CompanyId = company.Id, BranchId = branch.Id, AssignedByUserId = admin.Id,
                StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(30),
                // KRİTİK: tek hak kaldı.
                RemainingSessions = 1,
            };
            seed.PackageAssignments.Add(assignment);
            await seed.SaveChangesAsync();
            companyId = company.Id;
            adminId = admin.Id;
            packageAssignmentId = assignment.Id;
        }

        // TransactionBehavior'ın yaptığının aynısı (ITransactionalRequest).
        async Task<Exception?> CheckInAsync()
        {
            await using var context = database.CreateContext(companyId);
            var unitOfWork = new UnitOfWork(context);
            try
            {
                await unitOfWork.ExecuteWithRetryAsync(async () =>
                {
                    await using var transaction = await unitOfWork.BeginTransactionAsync();
                    try
                    {
                        await new RecordGeneralCheckInCommandHandler(unitOfWork).Handle(
                            new RecordGeneralCheckInCommand { PackageAssignmentId = packageAssignmentId, RequestedByUserId = adminId },
                            CancellationToken.None);
                        await unitOfWork.CommitTransactionAsync();
                        return true;
                    }
                    catch
                    {
                        await unitOfWork.RollbackTransactionAsync();
                        throw;
                    }
                });
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(CheckInAsync(), CheckInAsync());

        Assert.Single(results, r => r is null);
        Assert.IsType<NoRemainingSessionsException>(Assert.Single(results, r => r is not null));

        await using var verify = database.CreateContext();
        var remaining = (await verify.PackageAssignments.IgnoreQueryFilters().SingleAsync(a => a.Id == packageAssignmentId)).RemainingSessions;
        Assert.Equal(0, remaining);
        Assert.Equal(1, await verify.CheckIns.IgnoreQueryFilters().CountAsync(c => c.PackageAssignmentId == packageAssignmentId));
    }
}
