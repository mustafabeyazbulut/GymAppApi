using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.UpdateBranch;

public class UpdateBranchCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int BranchId { get; set; }

    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
