using GymAppApi.Application.Features.Auth.Common;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterComplete;

// Deliberately does NOT implement ITransactionalRequest - see this plan's
// "manual transaction" grounding fact. The wrong-code failure path must
// persist its AttemptCount increment independently of the success path's
// atomic User+RefreshToken+pending-cleanup segment, which the handler opens
// its own transaction around instead.
public class RegisterCompleteCommand : IRequest<AuthTokenResult>
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string PhoneCode { get; set; } = null!;
    public string? Email { get; set; }
    public string? EmailCode { get; set; }
    public string Password { get; set; } = null!;
}
