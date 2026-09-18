using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;

public class FreezePackageAssignmentCommand : IRequest
{
    // Set by the controller from the route segment.
    public int PackageAssignmentId { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
