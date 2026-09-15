using GymAppApi.Persistence.Context;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymAppApi.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public readonly string DbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-bytes-long",
                ["Jwt:Issuer"] = "GymAppApi.IntegrationTests",
                ["Jwt:Audience"] = "GymApp.IntegrationTests",
                ["Jwt:AccessTokenMinutes"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Removing just DbContextOptions<GymAppApiDbContext> isn't enough: EF Core
            // registers the original AddDbContext(...UseNpgsql...) callback from
            // Program.cs as a separate IDbContextOptionsConfiguration<GymAppApiDbContext>
            // service. If it's left in place, both that Npgsql-configuring delegate AND
            // our InMemory one get applied to the same DbContextOptionsBuilder when
            // options are built, which trips EF's "only a single database provider can
            // be registered" guard. Remove every registration tied to
            // GymAppApiDbContext before re-registering with the InMemory provider.
            var descriptorsToRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<GymAppApiDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(GymAppApiDbContext) ||
                (d.ServiceType.IsGenericType &&
                    d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>) &&
                    d.ServiceType.GetGenericArguments()[0] == typeof(GymAppApiDbContext))).ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<GymAppApiDbContext>(options => options.UseInMemoryDatabase(DbName));
        });
    }
}
