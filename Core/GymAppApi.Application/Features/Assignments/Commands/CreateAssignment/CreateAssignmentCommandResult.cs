namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

public class CreateAssignmentCommandResult
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public string Role { get; set; } = null!;
}
