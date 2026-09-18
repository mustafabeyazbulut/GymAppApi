using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;

public class UnfreezePackageAssignmentCommand : IRequest
{
    public int PackageAssignmentId { get; set; }
    public int RequestedByUserId { get; set; }
}
