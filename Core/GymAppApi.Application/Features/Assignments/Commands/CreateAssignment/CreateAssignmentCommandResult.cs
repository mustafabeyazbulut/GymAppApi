namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

// AssignmentId yok - henüz hiçbir şey atanmadı. Bu, az önce oluşturulan
// bekleyen daveti tanımlıyor; Assignment ancak davet edilen kişi onu
// onayladığında var olur (ConfirmAssignmentInvitationCommand).
public class CreateAssignmentCommandResult
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public string Role { get; set; } = null!;
}
