using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommand : IRequest<CreateBranchCommandResult>
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim, never
    // trusted from the body - the [Authorize(Policy = "GymAdminOnly")] policy
    // only proves the caller's active role is GymAdmin, not that it is scoped
    // to THIS request's CompanyId; the handler re-checks that. Sistem Sahibi
    // şube açamaz (senaryo §10.6).
    public int RequestedByUserId { get; set; }
}
