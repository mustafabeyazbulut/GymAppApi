using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// Deliberately NOT ITenantScoped/ICompanyScoped — belongs to a User, not a
// tenant. Listed in GymAppApiDbContext.IntentionallyUnscopedEntityTypes.
// Only TokenHash is ever persisted; the raw token is returned to the client
// once at issuance and never stored.
public class RefreshToken : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string TokenHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    // Postgres xmin concurrency token (see RefreshTokenConfiguration) —
    // MUST be a real CLR property, not a shadow property. This repository
    // pattern reads via AsNoTracking() by default and later calls Update()
    // on the same detached instance; a shadow property has no CLR storage
    // to round-trip through that gap, so EF has no "original value" to
    // compare and the concurrency check fails on every update, not just
    // races. A mapped CLR property gets materialized into the object even
    // when untracked, so Update() sees the real loaded value.
    public uint ConcurrencyToken { get; set; }
}
