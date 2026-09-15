using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

// Regression test for a real bug found during code review: RefreshToken's
// xmin-mapped concurrency token was originally a shadow property, which has
// no CLR storage to survive the AsNoTracking() read -> mutate -> Update()
// round trip this codebase's repository pattern always does. That made
// SaveChangesAsync throw DbUpdateConcurrencyException on every update, not
// just races. Fixed by making it a real CLR property (RefreshToken.cs).
// This test exercises the actual ReadRepository/WriteRepository/DbContext
// stack, not mocks, so it's the only test in this codebase that can catch
// this class of bug.
public class RefreshTokenConcurrencyTests
{
    private static GymAppApiDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new GymAppApiDbContext(options, new FakeTenantContext { IsSuperAdmin = true });
    }

    [Fact]
    public async Task Update_AfterAsNoTrackingRead_DoesNotThrow_WhenNoConcurrentModification()
    {
        var dbName = Guid.NewGuid().ToString();
        var user = new User { FullName = "Test", Phone = "+905550000000", PasswordHash = "x" };

        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Users.Add(user);
            seedContext.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = "hash",
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var readRepo = new ReadRepository<RefreshToken>(context);
        var writeRepo = new WriteRepository<RefreshToken>(context);

        // Default enableTracking: false, matching every handler's actual call shape.
        var token = await readRepo.GetAsync(t => t.UserId == user.Id);
        Assert.NotNull(token);

        token!.RevokedAt = DateTime.UtcNow;
        writeRepo.Update(token);

        // Before the CLR-property fix, this threw DbUpdateConcurrencyException
        // unconditionally (originalValue defaulted to 0, never matching the
        // real stored value) even though nothing else touched the row.
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Update_AfterAsNoTrackingRead_ThrowsConcurrencyException_WhenTokenChangedSinceRead()
    {
        // EF Core's InMemory provider doesn't auto-increment a RowVersion
        // column the way Postgres auto-increments xmin on every write (that
        // DB-side auto-generation is separately confirmed via
        // `dotnet ef migrations script`, which showed no real DDL for the
        // xmin mapping — provider-specific, can't be exercised without real
        // Postgres). What CAN be verified here, provider-agnostically, is
        // the actual bug/fix surface: whether EF's ChangeTracker correctly
        // detects a stale original value at all once a CLR property (not a
        // shadow property) carries it through the read/mutate/Update()
        // cycle. So this test manually changes the stored token value
        // (standing in for "Postgres already bumped xmin"), which is enough
        // to prove the detection path — not the auto-generation path.
        var dbName = Guid.NewGuid().ToString();
        var user = new User { FullName = "Test", Phone = "+905550000001", PasswordHash = "x" };

        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Users.Add(user);
            seedContext.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = "hash",
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                ConcurrencyToken = 1,
            });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var readRepo = new ReadRepository<RefreshToken>(context);
        var writeRepo = new WriteRepository<RefreshToken>(context);

        var token = await readRepo.GetAsync(t => t.UserId == user.Id);
        Assert.NotNull(token);
        Assert.Equal(1u, token!.ConcurrencyToken);

        // Simulate a concurrent request winning the race and Postgres
        // bumping xmin as a result: a separate, TRACKED read (so EF
        // correctly records the loaded value, 1, as this entry's original)
        // followed by an explicit bump to 2 lets EF see a genuine
        // original(1)->current(2) change and persist it — that succeeds
        // because it's the first writer and 1 still matches what's stored.
        await using (var concurrentContext = CreateContext(dbName))
        {
            var concurrentReadRepo = new ReadRepository<RefreshToken>(concurrentContext);
            var concurrentToken = await concurrentReadRepo.GetAsync(t => t.UserId == user.Id, enableTracking: true);
            concurrentToken!.RevokedAt = DateTime.UtcNow;
            concurrentToken.ConcurrencyToken = 2;
            await concurrentContext.SaveChangesAsync();
        }

        // `token` still carries the stale original value (1) it was loaded
        // with — the real row is now at 2.
        token.RevokedAt = DateTime.UtcNow;
        writeRepo.Update(token);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
    }
}
