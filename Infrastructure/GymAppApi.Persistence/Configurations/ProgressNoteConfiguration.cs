using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class ProgressNoteConfiguration : IEntityTypeConfiguration<ProgressNote>
{
    public void Configure(EntityTypeBuilder<ProgressNote> builder)
    {
        builder.Property(x => x.NoteText).HasMaxLength(1000);

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
