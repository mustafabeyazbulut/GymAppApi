using MediatR;

namespace GymAppApi.Application.Features.ContentLibrary.Commands.SetContentItemActive;

public class SetContentItemActiveCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int ContentItemId { get; set; }

    public bool IsActive { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
