using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.FreezeAccountRequestOtp;

// UserId is always overwritten server-side from the JWT `sub` claim by the
// controller after model binding - never trust a client-supplied UserId here.
public class FreezeAccountRequestOtpCommand : IRequest
{
    public int UserId { get; set; }
}
