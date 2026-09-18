using MediatR;

namespace GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentReservations;

public class GetPackageAssignmentReservationsQuery : IRequest<IReadOnlyList<ReservationDto>>
{
    public GetPackageAssignmentReservationsQuery(int packageAssignmentId, int requestedByUserId)
    {
        PackageAssignmentId = packageAssignmentId;
        RequestedByUserId = requestedByUserId;
    }

    public int PackageAssignmentId { get; }
    public int RequestedByUserId { get; }
}
