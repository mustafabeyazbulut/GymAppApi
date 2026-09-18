using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PackageAssignmentPaymentConfiguration : IEntityTypeConfiguration<PackageAssignmentPayment>
{
    public void Configure(EntityTypeBuilder<PackageAssignmentPayment> builder)
    {
        builder.Property(x => x.Amount).HasColumnType("decimal(10,2)");
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.PackageAssignment)
            .WithMany()
            .HasForeignKey(x => x.PackageAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PackageAssignmentId);
    }
}
