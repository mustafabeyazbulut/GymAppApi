using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

// Seeds exactly one SuperAdmin account so POST /api/assignments (GymAdmin/
// SuperAdmin-gated) can be exercised at all before any real admin-management
// tooling exists. PASSWORD IS A PLACEHOLDER — "ChangeMe123!SuperAdmin" is
// documented here in plaintext deliberately (so a future reader knows to
// change it) but MUST be rotated before any real production deployment; see
// the backend spec's "App Store / Play Store Yayın Standartları" section.
public class SuperAdminSeedConfiguration : IEntityTypeConfiguration<User>
{
    public const int SeedUserId = 1_000_000; // far outside normal auto-increment range, avoids collision
    public const int SeedAssignmentId = 1_000_000;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasData(new User
        {
            Id = SeedUserId,
            FullName = "GymApp SuperAdmin",
            Phone = "+900000000000",
            Email = "admin@gymapp.local",
            PasswordHash = "AQAAAAIAAYagAAAAEH3IHnPC7S7v0YMoHMqzARS4fXkwWC4uv1EQA2nXq9kmTl09mYL41BXzA2EU6OuOZg==",
            PreferredLanguage = "tr",
            PhoneVerified = true,
            CreatedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
