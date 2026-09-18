using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.QrCode).IsRequired().HasMaxLength(10);

        builder.HasOne(x => x.PackageAssignment)
            .WithMany()
            .HasForeignKey(x => x.PackageAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PackageAssignmentId);
        builder.HasIndex(x => new { x.TrainerId, x.ScheduledAt });
        builder.HasIndex(x => x.QrCode);
    }
}
