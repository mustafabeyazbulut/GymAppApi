using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(60);
        builder.Property(x => x.NameNormalized).IsRequired().HasMaxLength(60);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ad şube içinde tekil, büyük/küçük harf ve boşluk duyarsız: indeks
        // normalize edilmiş ad üzerinde (handler aynı anahtarla önden kontrol
        // eder; bu indeks eşzamanlı iki ekleme için son savunma).
        builder.HasIndex(x => new { x.BranchId, x.NameNormalized }).IsUnique();
    }
}
