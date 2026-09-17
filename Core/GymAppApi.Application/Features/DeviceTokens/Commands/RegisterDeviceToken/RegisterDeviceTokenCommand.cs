using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DeviceTokens.Commands.RegisterDeviceToken;

public class RegisterDeviceTokenCommand : IRequest
{
    public string Token { get; set; } = null!;
    public DevicePlatform Platform { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int UserId { get; set; }
}
