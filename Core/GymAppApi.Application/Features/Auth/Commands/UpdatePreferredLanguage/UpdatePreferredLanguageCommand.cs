using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;

// UserId is always overwritten server-side from the JWT `sub` claim by the
// controller after model binding, mirroring CreateAssignmentCommand's
// RequestedByUserId - never trust a client-supplied UserId here.
public class UpdatePreferredLanguageCommand : IRequest
{
    public int UserId { get; set; }
    public string Language { get; set; } = null!;
}
