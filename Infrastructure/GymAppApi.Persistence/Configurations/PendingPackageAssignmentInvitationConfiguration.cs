using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PendingPackageAssignmentInvitationConfiguration : IEntityTypeConfiguration<PendingPackageAssignmentInvitation>
{
    public void Configure(EntityTypeBuilder<PendingPackageAssignmentInvitation> builder)
    {
        builder.Property(x => x.Code).IsRequired().HasMaxLength(10);

        builder.HasOne(x => x.TargetUser)
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TargetUserId, x.CompanyId });
    }
}
