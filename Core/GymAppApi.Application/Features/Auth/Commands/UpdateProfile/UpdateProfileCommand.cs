using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.UpdateProfile;

// UserId is always overwritten server-side from the JWT `sub` claim by the
// controller, mirroring UpdatePreferredLanguageCommand - never trust a
// client-supplied UserId here. Phone is deliberately NOT editable here - it
// is the login identifier and OTP-verified at registration; changing it
// would need its own verify-new-number flow, out of scope for this command.
public class UpdateProfileCommand : IRequest
{
    public int UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
}
