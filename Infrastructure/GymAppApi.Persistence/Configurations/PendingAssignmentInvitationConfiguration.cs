using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PendingAssignmentInvitationConfiguration : IEntityTypeConfiguration<PendingAssignmentInvitation>
{
    public void Configure(EntityTypeBuilder<PendingAssignmentInvitation> builder)
    {
        builder.Property(x => x.Code).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(x => x.TargetUser)
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TargetUserId, x.CompanyId });
    }
}
