namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandResult
{
    public int AssignmentId { get; set; }
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public string Role { get; set; } = null!;
}
