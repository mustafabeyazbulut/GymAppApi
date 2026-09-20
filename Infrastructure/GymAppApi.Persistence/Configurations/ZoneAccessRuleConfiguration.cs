using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class ZoneAccessRuleConfiguration : IEntityTypeConfiguration<ZoneAccessRule>
{
    public void Configure(EntityTypeBuilder<ZoneAccessRule> builder)
    {
        builder.Property(x => x.RuleType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.RuleValue).HasMaxLength(100);

        builder.HasOne(x => x.Zone)
            .WithMany()
            .HasForeignKey(x => x.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
