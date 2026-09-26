using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PersonalLogConfiguration : IEntityTypeConfiguration<PersonalLog>
{
    public void Configure(EntityTypeBuilder<PersonalLog> builder)
    {
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Title).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.WeightKg).HasPrecision(5, 2);
        builder.Property(x => x.BodyFatPercent).HasPrecision(5, 2);
        builder.Property(x => x.WaistCm).HasPrecision(5, 2);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Liste sorgusu: kullanıcının tarih aralığındaki kayıtları.
        builder.HasIndex(x => new { x.UserId, x.Date });
    }
}
