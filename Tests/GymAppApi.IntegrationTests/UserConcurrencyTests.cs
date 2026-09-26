using System.Text.Json;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using GymAppApi.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymAppApi.IntegrationTests;

// User satırı xmin concurrency token taşır (RefreshToken ile aynı desen):
// AsNoTracking okuma -> değiştir -> Update() döngüsü tüm satırı yazdığı
// için, eşzamanlı başka bir yazma (ör. ResetPassword'ün yeni PasswordHash'i)
// eskiden bayat bir kopyayla sessizce geri alınabiliyordu. Artık kaybeden
// yazma DbUpdateConcurrencyException alır; API bunu 409 ConcurrentUpdate
// olarak döner.
public class UserConcurrencyTests
{
    private static GymAppApiDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeTenantContext { IsSuperAdmin = true });

    [Fact]
    public async Task StaleUserUpdate_AfterAConcurrentWrite_ThrowsInsteadOfOverwritingTheNewPasswordHash()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seed = CreateContext(dbName))
        {
            var user = new User { FullName = "Ayşe", Phone = "+905550000001", PasswordHash = "eski-hash", ConcurrencyToken = 1 };
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
            userId = user.Id;
        }

        // İstek A kullanıcıyı okudu (token=1)...
        await using var staleContext = CreateContext(dbName);
        var stale = await new ReadRepository<User>(staleContext).GetAsync(u => u.Id == userId);

        // ...bu arada ResetPassword yeni hash'i yazdı (Postgres xmin'i artırır;
        // InMemory artırmadığı için elle simüle ediliyor - RefreshTokenConcurrencyTests).
        await using (var resetContext = CreateContext(dbName))
        {
            var fresh = await resetContext.Users.SingleAsync(u => u.Id == userId);
            fresh.PasswordHash = "yeni-hash";
            fresh.ConcurrencyToken = 2;
            await resetContext.SaveChangesAsync();
        }

        stale!.FullName = "Ayşe Yılmaz";
        new WriteRepository<User>(staleContext).Update(stale);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
        await using var assertContext = CreateContext(dbName);
        Assert.Equal("yeni-hash", (await assertContext.Users.SingleAsync(u => u.Id == userId)).PasswordHash);
    }

    [Fact]
    public async Task UserUpdate_WithoutAConcurrentWrite_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        int userId;
        await using (var seed = CreateContext(dbName))
        {
            var user = new User { FullName = "Mert", Phone = "+905550000002", PasswordHash = "x" };
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
            userId = user.Id;
        }

        await using var context = CreateContext(dbName);
        var user2 = await new ReadRepository<User>(context).GetAsync(u => u.Id == userId);
        user2!.FullName = "Mert Kaya";
        new WriteRepository<User>(context).Update(user2);

        Assert.Null(await Record.ExceptionAsync(() => context.SaveChangesAsync()));
    }

    [Fact]
    public async Task ExceptionMiddleware_MapsAConcurrencyConflictTo409ConcurrentUpdate()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new DbUpdateConcurrencyException("Satır başka bir işlemle değişti."),
            NullLogger<ExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("ConcurrentUpdate", body.RootElement.GetProperty("Code").GetString());
    }
}
