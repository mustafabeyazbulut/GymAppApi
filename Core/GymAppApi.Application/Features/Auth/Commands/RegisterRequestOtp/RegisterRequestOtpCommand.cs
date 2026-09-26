using GymAppApi.Application.Common.RateLimiting;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommand : IRequest, IRateLimitedByIdentifier
{
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }

    string? IRateLimitedByIdentifier.RateLimitIdentifier => Phone;
}
