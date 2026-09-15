using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Register;

public class RegisterCommand : IRequest<RegisterCommandResult>
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string Password { get; set; } = null!;
}
