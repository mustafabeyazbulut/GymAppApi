using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommand : IRequest<AddStaffMemberCommandResult>
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public AssignmentRole Role { get; set; }
    public int BranchId { get; set; }

    // Set by the controller from the caller's own JWT sub claim - see this
    // plan's "Key facts" on why the [Authorize(Policy = "StaffManagement")]
    // attribute alone is never enough.
    public int RequestedByUserId { get; set; }
}
