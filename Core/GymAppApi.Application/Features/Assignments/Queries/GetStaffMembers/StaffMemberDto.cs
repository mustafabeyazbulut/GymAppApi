namespace GymAppApi.Application.Features.Assignments.Queries.GetStaffMembers;

public class StaffMemberDto
{
    public int AssignmentId { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string Role { get; set; } = null!;
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
}
