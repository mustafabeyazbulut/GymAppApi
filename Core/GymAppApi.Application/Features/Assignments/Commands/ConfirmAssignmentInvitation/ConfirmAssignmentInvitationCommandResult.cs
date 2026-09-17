namespace GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;

public class ConfirmAssignmentInvitationCommandResult
{
    public int AssignmentId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public string Role { get; set; } = null!;
}
