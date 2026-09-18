using MediatR;

namespace GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentTrainers;

public class GetPackageAssignmentTrainersQuery : IRequest<IReadOnlyList<TrainerDto>>
{
    public GetPackageAssignmentTrainersQuery(int packageAssignmentId, int requestedByUserId)
    {
        PackageAssignmentId = packageAssignmentId;
        RequestedByUserId = requestedByUserId;
    }

    public int PackageAssignmentId { get; }
    public int RequestedByUserId { get; }
}
