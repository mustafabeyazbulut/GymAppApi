using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

// Seeds exactly one SuperAdmin account so POST /api/assignments (GymAdmin/
// SuperAdmin-gated) can be exercised at all before any real admin-management
// tooling exists. PASSWORD IS A PLACEHOLDER — MUST be rotated before any
// real production deployment (see the backend spec's "App Store / Play
// Store Yayın Standartları" section). The plaintext value is deliberately
// NOT repeated here (only its hash is stored below) to avoid leaving a
// permanently grep-able credential in version control forever; the actual
// placeholder value is documented once, in
// docs/superpowers/plans/2026-09-15-real-auth.md's Task 12.
public class SuperAdminSeedConfiguration : IEntityTypeConfiguration<User>
{
    // Negative sentinel IDs — Postgres IDENTITY sequences default to
    // MINVALUE 1, so nextval() can never produce a negative number. This
    // makes "never collides with a real auto-generated row" a provable
    // guarantee, not just an improbable one (a large positive placeholder
    // like 1_000_000 would eventually collide once the sequence counts
    // that high from real registrations).
    public const int SeedUserId = -1;
    public const int SeedAssignmentId = -1;

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
