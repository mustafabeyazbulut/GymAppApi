namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommandResult
{
    public int PackageAssignmentId { get; set; }
    public int PackageId { get; set; }
    public DateTime? EndDate { get; set; }
}
