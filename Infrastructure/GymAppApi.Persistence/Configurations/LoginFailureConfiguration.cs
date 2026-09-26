using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class LoginFailureConfiguration : IEntityTypeConfiguration<LoginFailure>
{
    public void Configure(EntityTypeBuilder<LoginFailure> builder)
    {
        builder.Property(x => x.IpHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // Hesap+IP başına tek satır - eşzamanlı ilk ekleme yarışında ikinci
        // ekleme reddedilir ve LoginAttemptStore yeniden dener.
        builder.HasIndex(x => new { x.UserId, x.IpHash }).IsUnique();
    }
}
