using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.IntegrationTests;

// Gerçek satır kilidi (SELECT ... FOR UPDATE) davranışını doğrulayan yarış
// testleri için tek seferlik, izole Postgres veritabanı (bkz.
// ClassSessionCapacityRaceConditionTests'in aynı deseni). Her test kendi
// rastgele adlı veritabanını oluşturur ve sonunda siler - geliştirme
// veritabanına (gymapp_dev) dokunulmaz. Postgres'e erişilemiyorsa test
// atlanmış sayılır (Connected = false).
public sealed class PostgresRaceTestDatabase : IAsyncDisposable
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("GYMAPPAPI_TEST_POSTGRES_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=gymappapi_race_test;Username=postgres;Password=postgres";

    public string DatabaseName { get; } = $"gymappapi_race_test_{Guid.NewGuid():N}";
    public bool Connected { get; private set; }

    public static async Task<PostgresRaceTestDatabase> CreateAsync()
    {
        var database = new PostgresRaceTestDatabase();
        try
        {
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            database.Connected = true;
        }
        catch
        {
            database.Connected = false;
        }
        return database;
    }

    public GymAppApiDbContext CreateContext(int? companyId = null)
    {
        var connectionString = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = DatabaseName }.ToString();
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>().UseNpgsql(connectionString).Options;
        return new GymAppApiDbContext(options, new AmbientTenantContext { CompanyId = companyId });
    }

    public async ValueTask DisposeAsync()
    {
        if (!Connected)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
