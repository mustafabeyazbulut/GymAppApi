using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class DoorConfiguration : IEntityTypeConfiguration<Door>
{
    public void Configure(EntityTypeBuilder<Door> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        builder.HasOne(x => x.Zone)
            .WithMany()
            .HasForeignKey(x => x.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
