using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Register;

// ITransactionalRequest: the handler's two SaveChangesAsync calls (User, then
// RefreshToken) must commit or roll back together — without this, a failure
// on the second save would leave a User row persisted with no RefreshToken.
public class RegisterCommand : IRequest<RegisterCommandResult>, ITransactionalRequest
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string Password { get; set; } = null!;
}
