using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// Deliberately NOT ITenantScoped/ICompanyScoped - a notification belongs to
// exactly one User regardless of which company/branch triggered it (mirrors
// DeviceToken, which is user-owned for the same reason).
public class Notification : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
