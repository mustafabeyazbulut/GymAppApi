using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommand : IRequest<CreateBranchCommandResult>
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim, never
    // trusted from the body - the [Authorize(Policy = "GymAdminOrSuperAdmin")]
    // policy only proves the caller holds SOME such role somewhere, not one
    // scoped to THIS request's CompanyId. The handler re-checks that a
    // GymAdmin caller is scoped to this exact company (SuperAdmin bypasses
    // the check).
    public int RequestedByUserId { get; set; }
}
