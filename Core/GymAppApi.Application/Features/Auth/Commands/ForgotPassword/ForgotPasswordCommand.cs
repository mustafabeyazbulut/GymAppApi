using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommand : IRequest<ForgotPasswordCommandResult>
{
    public string Identifier { get; set; } = null!; // phone or email
}
