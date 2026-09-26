using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Seeding;
using GymAppApi.Domain.Entities;
using GymAppApi.Infrastructure.Security;
using Moq;

namespace GymAppApi.UnitTests.Common.Seeding;

public class SuperAdminPhoneSeederTests
{
    private static (SuperAdminPhoneSeeder seeder, Mock<IWriteRepository<User>> writeRepo, Mock<IUnitOfWork> uow) Create(User? seedUser)
    {
        var readRepo = new Mock<IReadRepository<User>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, true, default))
            .ReturnsAsync(seedUser);
        var writeRepo = new Mock<IWriteRepository<User>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(writeRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (new SuperAdminPhoneSeeder(uow.Object, new PhoneNumberNormalizer()), writeRepo, uow);
    }

    private static User SeedUser(string phone) => new()
    {
        Id = SuperAdminPhoneSeeder.SeedUserId, FullName = "GymApp SuperAdmin", Phone = phone, PasswordHash = "x",
    };

    [Fact]
    public async Task ApplyAsync_WhenPlaceholderAndValidPhoneConfigured_UpdatesToCanonicalE164()
    {
        var user = SeedUser(SuperAdminPhoneSeeder.PlaceholderPhone);
        var (seeder, writeRepo, uow) = Create(user);

        var outcome = await seeder.ApplyAsync("0555 000 10 00", CancellationToken.None);

        Assert.Equal(SuperAdminPhoneSeedOutcome.Updated, outcome);
        Assert.Equal("+905550001000", user.Phone);
        writeRepo.Verify(r => r.Update(user), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WhenPhoneWasAlreadyChangedManually_LeavesItAlone_EvenIfConfigDiffers()
    {
        // Dev DB'de SA telefonu elle +905550001000 yapılmış durumda - yapılandırma
        // farklı bir değer verse bile elle yapılan değişikliğin üzerine yazılmaz.
        var user = SeedUser("+905550001000");
        var (seeder, writeRepo, uow) = Create(user);

        var outcome = await seeder.ApplyAsync("+905559990000", CancellationToken.None);

        Assert.Equal(SuperAdminPhoneSeedOutcome.AlreadyCustomized, outcome);
        Assert.Equal("+905550001000", user.Phone);
        writeRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_WhenPlaceholderAndNothingConfigured_ReportsPlaceholderRemains()
    {
        var user = SeedUser(SuperAdminPhoneSeeder.PlaceholderPhone);
        var (seeder, writeRepo, _) = Create(user);

        var outcome = await seeder.ApplyAsync(null, CancellationToken.None);

        Assert.Equal(SuperAdminPhoneSeedOutcome.PlaceholderRemains, outcome);
        writeRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_WhenSeedUserDoesNotExist_DoesNothing()
    {
        var (seeder, writeRepo, _) = Create(seedUser: null);

        var outcome = await seeder.ApplyAsync("+905550001000", CancellationToken.None);

        Assert.Equal(SuperAdminPhoneSeedOutcome.SeedUserMissing, outcome);
        writeRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Theory]
    [InlineData("+900000000000")]
    [InlineData("not-a-phone")]
    [InlineData("+90555")]
    public async Task ApplyAsync_WhenConfiguredPhoneIsInvalid_ThrowsWithAClearMessage(string configured)
    {
        var (seeder, _, _) = Create(SeedUser(SuperAdminPhoneSeeder.PlaceholderPhone));

        var ex = await Assert.ThrowsAsync<SeedConfigurationException>(() => seeder.ApplyAsync(configured, CancellationToken.None));

        Assert.Contains("Seed:SuperAdminPhone", ex.Message);
    }
}
