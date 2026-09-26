using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.AccessTier).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Price).HasColumnType("decimal(10,2)");
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CompanyId);

        // Package <-> Service çoka-çok (PackageServices tablosu). Paket
        // silinirse bağlantılar gider; kullanılan bir hizmet silinemez (soft close).
        builder.HasMany(x => x.Services)
            .WithMany()
            .UsingEntity<PackageService>(
                join => join.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict),
                join => join.HasOne<Package>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("PackageServices");
                    join.HasKey(x => new { x.PackageId, x.ServiceId });
                });
    }
}
