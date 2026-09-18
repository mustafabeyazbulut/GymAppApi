namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

// No PackageAssignmentId - nothing is assigned yet, only invited. The real
// PackageAssignment only comes into existence once the member confirms
// (ConfirmPackageAssignmentCommand).
public class CreatePackageAssignmentCommandResult
{
    public int UserId { get; set; }
    public int PackageId { get; set; }
}
