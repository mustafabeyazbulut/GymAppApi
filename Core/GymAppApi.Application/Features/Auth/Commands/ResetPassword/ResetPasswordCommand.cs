using GymAppApi.Application.Common.RateLimiting;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommand : IRequest, IRateLimitedByIdentifier
{
    public string Identifier { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string NewPassword { get; set; } = null!;

    string? IRateLimitedByIdentifier.RateLimitIdentifier => Identifier;
}
