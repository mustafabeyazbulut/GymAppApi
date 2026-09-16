using GymAppApi.Application.Features.Auth.Common;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Refresh;

public class RefreshCommand : IRequest<AuthTokenResult>
{
    public string RefreshToken { get; set; } = null!;
}
