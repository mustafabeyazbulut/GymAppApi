using GymAppApi.Application.Common.RateLimiting;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommand : IRequest<ForgotPasswordCommandResult>, IRateLimitedByIdentifier
{
    public string Identifier { get; set; } = null!; // phone or email

    string? IRateLimitedByIdentifier.RateLimitIdentifier => Identifier;
}
