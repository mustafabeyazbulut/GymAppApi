using GymAppApi.Application.Features.Auth.Commands.Register;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Refresh;

public class RefreshCommand : IRequest<RegisterCommandResult>
{
    public string RefreshToken { get; set; } = null!;
}
