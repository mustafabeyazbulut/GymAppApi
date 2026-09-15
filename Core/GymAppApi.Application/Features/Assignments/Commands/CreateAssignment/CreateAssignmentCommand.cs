using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

public class CreateAssignmentCommand : IRequest<CreateAssignmentCommandResult>
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }

    // Set by the controller from the caller's own JWT sub claim, never from
    // the request body — the [Authorize(Policy = "GymAdminOrSuperAdmin")]
    // policy only confirms the caller holds SOME GymAdmin/SuperAdmin
    // assignment somewhere, not one scoped to THIS request's CompanyId.
    // Without this, a GymAdmin of Company A could assign members into an
    // unrelated Company B. The handler re-checks this per-company.
    public int RequestedByUserId { get; set; }
}
