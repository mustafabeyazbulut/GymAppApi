using GymAppApi.Application.Common.Security;
using GymAppApi.Domain.Entities;
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

    // Günlük temizlik (LoginFailureCleanupHostedService): hem kilidi hem son
    // güncellemesi 30 günden eski satırlar silinir; yeni ya da hâlâ ileri
    // tarihli kilidi olan satırlar kalır. Otomatik zaman damgası geçmiş tarih
    // yazmaya izin vermediği için "şimdi" ileri alınarak simüle ediliyor.
    [Fact]
    public async Task PurgeStale_DeletesRowsWhoseLockAndLastUpdateAreOlderThanRetention()
    {
        var dbName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        await using (var context = CreateContext(dbName))
        {
            context.LoginFailures.AddRange(
                new LoginFailure { UserId = 1, IpHash = "eski-kilitsiz", FailedCount = 2 },
                new LoginFailure { UserId = 2, IpHash = "eski-kilidi-bitmis", LockedUntil = now.AddMinutes(15) },
                new LoginFailure { UserId = 3, IpHash = "kilidi-hala-ileride", LockedUntil = now.AddDays(35) });
            await context.SaveChangesAsync();
        }

        int deleted;
        await using (var context = CreateContext(dbName))
        {
            deleted = await new LoginAttemptStore(context).PurgeStaleAsync(now.AddDays(40), CancellationToken.None);
        }

        await using (var context = CreateContext(dbName))
        {
            var remaining = await context.LoginFailures.Select(f => f.IpHash).ToListAsync();
            Assert.Equal(2, deleted);
            Assert.Equal(new[] { "kilidi-hala-ileride" }, remaining);
        }
    }

    [Fact]
    public async Task PurgeStale_KeepsRowsUpdatedWithinRetention()
    {
        var dbName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        await FailAsync(dbName, IpA, now, times: 1);

        await using var context = CreateContext(dbName);
        var deleted = await new LoginAttemptStore(context).PurgeStaleAsync(now.AddDays(20), CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await context.LoginFailures.CountAsync());
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
