using MediatR;

namespace GymAppApi.Application.Features.Auth.Queries.GetMe;

public class GetMeQuery : IRequest<MeResultDto>
{
    public int UserId { get; set; }
}
