using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class OtpVerification : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public OtpPurpose Purpose { get; set; }
    public bool IsUsed { get; set; }
    public int AttemptCount { get; set; }
}
