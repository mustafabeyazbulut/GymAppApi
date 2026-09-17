using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;

public class RemoveAssignmentCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int AssignmentId { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
