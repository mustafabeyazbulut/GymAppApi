namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

// No AssignmentId - nothing is assigned yet. This describes the pending
// invitation that was just issued; the Assignment itself only comes into
// existence once the invitee confirms it (ConfirmAssignmentInvitationCommand).
public class AddStaffMemberCommandResult
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public string Role { get; set; } = null!;
}
