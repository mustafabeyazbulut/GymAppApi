using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;

// ITransactionalRequest: GymAdmin kaldırılırken firma satırı FOR UPDATE ile
// kilitlenir (son Gym Admin kontrolü yarış güvenli olsun) - kilit ancak
// TransactionBehavior'ın açtığı transaction içinde anlamlı.
public class RemoveAssignmentCommand : IRequest, ITransactionalRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int AssignmentId { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
