using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class DeviceToken : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Token { get; set; } = null!;
    public DevicePlatform Platform { get; set; }
}
