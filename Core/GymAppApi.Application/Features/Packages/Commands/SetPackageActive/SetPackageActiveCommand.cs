using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.SetPackageActive;

public class SetPackageActiveCommand : IRequest
{
    // Set by the controller from the route segment.
    public int PackageId { get; set; }
    public bool IsActive { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
