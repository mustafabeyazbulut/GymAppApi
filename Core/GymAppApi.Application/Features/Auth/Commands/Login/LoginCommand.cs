using GymAppApi.Application.Features.Auth.Common;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Login;

public class LoginCommand : IRequest<AuthTokenResult>
{
    public string Identifier { get; set; } = null!; // phone or email
    public string Password { get; set; } = null!;
}
