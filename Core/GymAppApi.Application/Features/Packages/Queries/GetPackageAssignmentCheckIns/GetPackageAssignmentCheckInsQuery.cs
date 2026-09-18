using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentCheckIns;

public class GetPackageAssignmentCheckInsQuery : IRequest<IReadOnlyList<CheckInDto>>
{
    public GetPackageAssignmentCheckInsQuery(int packageAssignmentId, int requestedByUserId)
    {
        PackageAssignmentId = packageAssignmentId;
        RequestedByUserId = requestedByUserId;
    }

    public int PackageAssignmentId { get; }
    public int RequestedByUserId { get; }
}
