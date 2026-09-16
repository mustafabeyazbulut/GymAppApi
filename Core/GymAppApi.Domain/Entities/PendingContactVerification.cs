using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Not tied to a User: this row exists ONLY during registration, before any
// User exists ("verify first, create second" — see
// docs/superpowers/specs/2026-09-16-register-phone-verification-design.md).
// One active row per (Channel, Target): request-otp upserts it (regenerating
// Code/ExpiresAt/AttemptCount, tracking LastSentAt/SendCount/WindowStartAt
// for the 60s/hourly-5 send limit), complete deletes it once consumed.
public class PendingContactVerification : EntityBase
{
    public ContactChannel Channel { get; set; }
    public string Target { get; set; } = null!;
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime LastSentAt { get; set; }
    public int SendCount { get; set; }
    public DateTime WindowStartAt { get; set; }
}
