using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class SuperAdminAssignmentSeedConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.HasData(new Assignment
        {
            Id = SuperAdminSeedConfiguration.SeedAssignmentId,
            UserId = SuperAdminSeedConfiguration.SeedUserId,
            CompanyId = null,
            BranchId = null,
            Role = AssignmentRole.SuperAdmin,
            IsActive = true,
            CreatedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
