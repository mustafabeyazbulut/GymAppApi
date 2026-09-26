using GymAppApi.Application.Common.Security;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

// Hesap+IP bazlı başarısız giriş sayacı/kilidi (LoginFailures tablosu).
// Gerçek DbContext ile - sayaç artırma eşzamanlı isteklerde kayıp güncelleme
// (lost update) yaşamamalı.
public class LoginAttemptStoreTests
{
    private const int UserId = 7;
    private const string IpA = "ip-hash-a";
    private const string IpB = "ip-hash-b";

    private static GymAppApiDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeTenantContext { IsSuperAdmin = false });

    private static async Task FailAsync(string dbName, string ipHash, DateTime now, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await using var context = CreateContext(dbName);
            await new LoginAttemptStore(context).RecordFailureAsync(UserId, ipHash, now, CancellationToken.None);
        }
    }

    private static async Task<bool> IsLockedAsync(string dbName, string ipHash, DateTime now)
    {
        await using var context = CreateContext(dbName);
        return await new LoginAttemptStore(context).IsLockedAsync(UserId, ipHash, now, CancellationToken.None);
    }

    [Fact]
    public async Task AfterMaxFailuresFromOneIp_ThatIpIsLocked_ButAnotherIpIsNot()
    {
        var dbName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await FailAsync(dbName, IpA, now, LoginLockoutPolicy.MaxFailedAttempts - 1);
        Assert.False(await IsLockedAsync(dbName, IpA, now));

        await FailAsync(dbName, IpA, now, 1);

        Assert.True(await IsLockedAsync(dbName, IpA, now));
        // Kilit DoS'u yok: saldırganın IP'si kurbanın kendi IP'sini kilitlemez.
        Assert.False(await IsLockedAsync(dbName, IpB, now));
    }

    [Fact]
    public async Task LockExpiresAfterTheLockoutDuration_AndTheCounterStartsOver()
    {
        var dbName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        await FailAsync(dbName, IpA, now, LoginLockoutPolicy.MaxFailedAttempts);

        var later = now.Add(LoginLockoutPolicy.LockoutDuration).AddSeconds(1);

        Assert.False(await IsLockedAsync(dbName, IpA, later));
        await FailAsync(dbName, IpA, later, 1);
        Assert.False(await IsLockedAsync(dbName, IpA, later));
    }

    [Fact]
    public async Task Reset_ClearsOnlyThatIp_ClearAllForUser_ClearsEveryIp()
    {
        var dbName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        await FailAsync(dbName, IpA, now, LoginLockoutPolicy.MaxFailedAttempts);
        await FailAsync(dbName, IpB, now, LoginLockoutPolicy.MaxFailedAttempts);

        await using (var context = CreateContext(dbName))
        {
            await new LoginAttemptStore(context).ResetAsync(UserId, IpA, CancellationToken.None);
        }
        Assert.False(await IsLockedAsync(dbName, IpA, now));
        Assert.True(await IsLockedAsync(dbName, IpB, now));

        await using (var context = CreateContext(dbName))
        {
            await new LoginAttemptStore(context).ClearAllForUserAsync(UserId, CancellationToken.None);
        }
        Assert.False(await IsLockedAsync(dbName, IpB, now));
    }

    [Fact]
    public async Task ParallelFailures_AreNotLost_AndTheLockEngages()
    {
        // Kayıp güncelleme olsaydı eşzamanlı 12 yanlış deneme sayacı düşük
        // bırakır ve kilit hiç devreye girmezdi. İlk deneme satırı oluştursun
        // diye sıralı (InMemory unique index'i zorlamadığı için eşzamanlı ilk
        // ekleme Postgres'teki gibi reddedilmez).
        var dbName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        await FailAsync(dbName, IpA, now, 1);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            await using var context = CreateContext(dbName);
            await new LoginAttemptStore(context).RecordFailureAsync(UserId, IpA, now, CancellationToken.None);
        })));

        Assert.True(await IsLockedAsync(dbName, IpA, now));
    }
}
