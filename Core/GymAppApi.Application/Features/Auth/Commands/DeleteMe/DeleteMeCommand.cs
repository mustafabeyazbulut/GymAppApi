using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.DeleteMe;

public class DeleteMeCommand : IRequest
{
    public int UserId { get; set; }
}
