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
}
