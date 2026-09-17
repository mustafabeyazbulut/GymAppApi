using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.SetBranchActive;

public class SetBranchActiveCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int BranchId { get; set; }

    public bool IsActive { get; set; }

    // Set by the controller from the caller's own JWT sub claim - see
    // CreateBranchCommand for why this re-check is needed on this
    // controller (unlike CompaniesController's SuperAdminOnly actions).
    public int RequestedByUserId { get; set; }
}
