using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Seeding;
using GymAppApi.Domain.Entities;
using GymAppApi.Infrastructure.Security;
using GymAppApi.WebApi.BackgroundServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GymAppApi.IntegrationTests;

// Açılış servisi: yapılandırma hataları açılışı durdurur, geçici DB erişim
// hatası (taze/migration uygulanmamış DB, kesinti) ise sadece loglanır ve
// API'nin açılmasına izin verilir.
public class SuperAdminPhoneSeedHostedServiceTests
{
    private static SuperAdminPhoneSeedHostedService Create(Mock<IUnitOfWork> uow, string? configuredPhone, string environmentName)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new SuperAdminPhoneSeeder(uow.Object, new PhoneNumberNormalizer()));
        var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SuperAdminPhoneSeeder.ConfigurationKey] = configuredPhone })
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        return new SuperAdminPhoneSeedHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(), configuration, environment.Object,
            NullLogger<SuperAdminPhoneSeedHostedService>.Instance);
    }

    private static Mock<IUnitOfWork> UnitOfWorkReturning(User? seedUser)
    {
        var readRepo = new Mock<IReadRepository<User>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedUser);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(new Mock<IWriteRepository<User>>().Object);
        return uow;
    }

    private static Mock<IUnitOfWork> UnitOfWorkThatCannotReachTheDatabase()
    {
        var readRepo = new Mock<IReadRepository<User>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Veritabanına bağlanılamadı."));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);
        return uow;
    }

    private static Mock<IUnitOfWork> UnitOfWorkThrowing(Exception exception)
    {
        var readRepo = new Mock<IReadRepository<User>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(readRepo.Object);
        return uow;
    }

    public static TheoryData<Exception> InfrastructureFailures => new()
    {
        new TimeoutException("Zaman aşımı."),
        new System.Net.Sockets.SocketException(),
        new Npgsql.NpgsqlException("Bağlantı kurulamadı.", new System.Net.Sockets.SocketException()),
    };

    [Theory]
    [MemberData(nameof(InfrastructureFailures))]
    public async Task StartAsync_WhenAnInfrastructureFailureOccurs_DoesNotBlockStartup(Exception failure)
    {
        var service = Create(UnitOfWorkThrowing(failure), configuredPhone: "+905550001000", "Production");

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenAUniqueConstraintIsViolated_StopsStartup()
    {
        // Sunucu tarafı bir veri hatası (ör. telefon başka bir kullanıcıda
        // kayıtlı - unique index ihlali) "DB'ye erişilemedi" diye yutulmamalı.
        var uniqueViolation = new Microsoft.EntityFrameworkCore.DbUpdateException(
            "Kayıt güncellenemedi.",
            new Npgsql.PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505"));
        var service = Create(UnitOfWorkThrowing(uniqueViolation), configuredPhone: "+905550001000", "Production");

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WhenAnUnexpectedErrorOccurs_StopsStartup()
    {
        var service = Create(UnitOfWorkThrowing(new InvalidCastException("Beklenmeyen.")), configuredPhone: "+905550001000", "Production");

        await Assert.ThrowsAsync<InvalidCastException>(() => service.StartAsync(CancellationToken.None));
    }

    private static User PlaceholderSeedUser() => new()
    {
        Id = SuperAdminPhoneSeeder.SeedUserId, FullName = "SA", Phone = SuperAdminPhoneSeeder.PlaceholderPhone, PasswordHash = "x",
    };

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task StartAsync_WhenTheDatabaseIsUnreachable_DoesNotBlockStartup(string environment)
    {
        var service = Create(UnitOfWorkThatCannotReachTheDatabase(), configuredPhone: "+905550001000", environment);

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenConfiguredPhoneIsInvalid_StopsStartup_EvenIfTheDatabaseIsUnreachable()
    {
        var service = Create(UnitOfWorkThatCannotReachTheDatabase(), configuredPhone: "+900000000000", "Production");

        await Assert.ThrowsAsync<SeedConfigurationException>(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WhenPlaceholderRemainsOutsideDevelopment_StopsStartup()
    {
        var service = Create(UnitOfWorkReturning(PlaceholderSeedUser()), configuredPhone: null, "Production");

        await Assert.ThrowsAsync<SeedConfigurationException>(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WhenPlaceholderRemainsInDevelopment_OnlyWarns()
    {
        var service = Create(UnitOfWorkReturning(PlaceholderSeedUser()), configuredPhone: null, "Development");

        await service.StartAsync(CancellationToken.None);
    }
}
