using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PendingContactVerificationConfiguration : IEntityTypeConfiguration<PendingContactVerification>
{
    public void Configure(EntityTypeBuilder<PendingContactVerification> builder)
    {
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        // 320 = RFC 5321 max email length; comfortably covers E.164 phone too (max 16 chars incl. '+').
        builder.Property(x => x.Target).IsRequired().HasMaxLength(320);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(6);

        builder.HasIndex(x => new { x.Channel, x.Target }).IsUnique();
    }
}
