using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(200);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);

        // Postgres system column, no new DB column — protects against a
        // TOCTOU race where two concurrent refresh requests both read this
        // row as not-yet-revoked and both proceed to rotate it, which would
        // let a stolen token's reuse-detection never fire (see the Real
        // Auth backend plan's Task 7 code-quality review for the full
        // scenario).
        //
        // Npgsql's NpgsqlPostgresModelFinalizingConvention auto-detects any
        // uint property configured ValueGeneratedOnAddOrUpdate + concurrency
        // token and maps it to the existing "xmin" system column — no new
        // DB column, no migration needed for the mapping itself. This MUST
        // be a real CLR property (RefreshToken.ConcurrencyToken), not a
        // shadow property — see that property's own doc comment for why a
        // shadow property silently breaks the concurrency check on every
        // Update() call through this codebase's AsNoTracking()-by-default
        // repository pattern, not just under a race.
        builder.Property(x => x.ConcurrencyToken).IsRowVersion();
    }
}
