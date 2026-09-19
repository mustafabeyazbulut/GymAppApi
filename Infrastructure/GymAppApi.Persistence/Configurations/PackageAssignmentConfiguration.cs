using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PackageAssignmentConfiguration : IEntityTypeConfiguration<PackageAssignment>
{
    public void Configure(EntityTypeBuilder<PackageAssignment> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.TotalFrozenDays).HasDefaultValue(0);

        builder.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.MemberUser)
            .WithMany(u => u.PackageAssignments)
            .HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CompanyId, x.BranchId });
        builder.HasIndex(x => x.MemberUserId);
    }
}
