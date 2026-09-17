using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommand : IRequest<InviteGymAdminCommandResult>
{
    public int CompanyId { get; set; }

    // Looks up an already-registered user by phone and invites them to be a
    // GymAdmin of this company too - this never creates a new User, same
    // rule as CreateCompanyCommand/AddStaffMemberCommand.
    public string Phone { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim - never
    // trusted from the body. The [Authorize(Policy = "GymAdminOrSuperAdmin")]
    // policy only proves the caller holds SOME such role somewhere; the
    // handler re-checks a GymAdmin caller is scoped to THIS CompanyId.
    public int RequestedByUserId { get; set; }
}
