using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class ClassEnrollmentConfiguration : IEntityTypeConfiguration<ClassEnrollment>
{
    public void Configure(EntityTypeBuilder<ClassEnrollment> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(x => x.ClassSession)
            .WithMany()
            .HasForeignKey(x => x.ClassSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PackageAssignment)
            .WithMany()
            .HasForeignKey(x => x.PackageAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ClassSessionId);
        builder.HasIndex(x => x.PackageAssignmentId);
        builder.HasIndex(x => x.MemberUserId);
    }
}
