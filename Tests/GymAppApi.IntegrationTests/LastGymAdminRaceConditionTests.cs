using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GymAppApi.IntegrationTests;

// İnceleme bulgusu [85]: LastGymAdminGuard sadece SELECT yapıyordu; firmanın
// iki Gym Admin'i aynı anda hesabını silerse ikisi de "başka admin var"
// okuyup firma Gym Admin'siz kalıyordu. Artık kontrol + silme tek
// transaction'da ve firma satırı FOR UPDATE ile kilitli - ikinci istek güncel
// durumu görür ve 409 LastGymAdmin alır. Gerçek kilit sadece Postgres'te
// doğrulanabilir (InMemory'de kilit yok).
public class LastGymAdminRaceConditionTests
{
    private const string Code = "123456";
    private readonly ITestOutputHelper _output;

    public LastGymAdminRaceConditionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task TwoGymAdminsDeletingTheirAccountsAtTheSameTime_LeaveTheCompanyWithOneGymAdmin()
    {
        await using var database = await PostgresRaceTestDatabase.CreateAsync();
        if (!database.Connected)
        {
            _output.WriteLine("Postgres'e erişilemedi - yarış testi atlandı.");
            return;
        }

        int companyId;
        int adminAId;
        int adminBId;
        await using (var seed = database.CreateContext())
        {
            var company = new Company { Name = "Firma", IsActive = true };
            seed.Companies.Add(company);
            var adminA = new User { FullName = "Admin A", Phone = "+905550007001", PasswordHash = "x" };
            var adminB = new User { FullName = "Admin B", Phone = "+905550007002", PasswordHash = "x" };
            seed.Users.AddRange(adminA, adminB);
            await seed.SaveChangesAsync();
            seed.Assignments.AddRange(
                new Assignment { UserId = adminA.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true },
                new Assignment { UserId = adminB.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
            foreach (var phone in new[] { adminA.Phone, adminB.Phone })
            {
                seed.PendingContactVerifications.Add(new PendingContactVerification
                {
                    Channel = ContactChannel.Phone, Target = phone, Code = Code, ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                    AttemptCount = 0, LastSentAt = DateTime.UtcNow, SendCount = 1, WindowStartAt = DateTime.UtcNow,
                });
            }
            await seed.SaveChangesAsync();
            companyId = company.Id;
            adminAId = adminA.Id;
            adminBId = adminB.Id;
        }

        async Task<Exception?> DeleteAsync(int userId)
        {
            await using var context = database.CreateContext();
            try
            {
                await new DeleteMeCommandHandler(new UnitOfWork(context))
                    .Handle(new DeleteMeCommand { UserId = userId, Code = Code }, CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(DeleteAsync(adminAId), DeleteAsync(adminBId));

        Assert.Single(results, r => r is null);
        Assert.IsType<LastGymAdminException>(Assert.Single(results, r => r is not null));

        await using var verify = database.CreateContext();
        var remainingAdmins = await verify.Assignments.IgnoreQueryFilters()
            .CountAsync(a => a.CompanyId == companyId && a.Role == AssignmentRole.GymAdmin && a.IsActive);
        Assert.Equal(1, remainingAdmins);
    }
}
