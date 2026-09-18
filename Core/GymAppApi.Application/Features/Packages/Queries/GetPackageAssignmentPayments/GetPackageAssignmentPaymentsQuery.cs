using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentPayments;

public class GetPackageAssignmentPaymentsQuery : IRequest<GetPackageAssignmentPaymentsResult>
{
    public GetPackageAssignmentPaymentsQuery(int packageAssignmentId, int requestedByUserId)
    {
        PackageAssignmentId = packageAssignmentId;
        RequestedByUserId = requestedByUserId;
    }

    public int PackageAssignmentId { get; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; }
}
