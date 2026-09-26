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

        builder.HasIndex(x => new { x.TargetUserId, x.CompanyId });
        // xmin sistem kolonuna eşlenir, yeni DB kolonu yok (RefreshTokenConfiguration).
        builder.Property(x => x.ConcurrencyToken).IsRowVersion();
    }
}
