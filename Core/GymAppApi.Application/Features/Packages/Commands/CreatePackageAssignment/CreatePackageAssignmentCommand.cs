using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommand : IRequest<CreatePackageAssignmentCommandResult>
{
    public int PackageId { get; set; }
    // Looks up an already-registered user by phone - never creates a new User.
    public string MemberPhone { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
