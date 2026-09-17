using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommand : IRequest<AddStaffMemberCommandResult>
{
    // Looks up an already-registered user by phone and attaches an
    // Assignment to them — this never creates a new User. See
    // .claude/memory/feedback-never-remove-registration-pointer.md for why.
    public string Phone { get; set; } = null!;
    public AssignmentRole Role { get; set; }
    public int BranchId { get; set; }

    // Set by the controller from the caller's own JWT sub claim - see this
    // plan's "Key facts" on why the [Authorize(Policy = "StaffManagement")]
    // attribute alone is never enough.
    public int RequestedByUserId { get; set; }
}
