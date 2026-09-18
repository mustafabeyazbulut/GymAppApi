using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class CheckInConfiguration : IEntityTypeConfiguration<CheckIn>
{
    public void Configure(EntityTypeBuilder<CheckIn> builder)
    {
        builder.HasOne(x => x.PackageAssignment)
            .WithMany()
            .HasForeignKey(x => x.PackageAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Reservation)
            .WithMany()
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PackageAssignmentId);
    }
}
