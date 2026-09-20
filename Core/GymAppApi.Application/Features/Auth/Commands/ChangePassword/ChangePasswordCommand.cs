using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ChangePassword;

// UserId is always overwritten server-side from the JWT `sub` claim by the
// controller, mirroring UpdatePreferredLanguageCommand.
public class ChangePasswordCommand : IRequest
{
    public int UserId { get; set; }
    public string CurrentPassword { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
}
