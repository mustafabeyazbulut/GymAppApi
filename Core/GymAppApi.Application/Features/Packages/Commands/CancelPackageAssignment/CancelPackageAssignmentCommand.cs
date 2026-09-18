using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;

public class CancelPackageAssignmentCommand : IRequest
{
    public int PackageAssignmentId { get; set; }
    public int RequestedByUserId { get; set; }
}
