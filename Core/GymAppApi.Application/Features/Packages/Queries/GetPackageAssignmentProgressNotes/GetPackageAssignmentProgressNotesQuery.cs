using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentProgressNotes;

public class GetPackageAssignmentProgressNotesQuery : IRequest<IReadOnlyList<ProgressNoteDto>>
{
    public GetPackageAssignmentProgressNotesQuery(int packageAssignmentId, int requestedByUserId)
    {
        PackageAssignmentId = packageAssignmentId;
        RequestedByUserId = requestedByUserId;
    }

    public int PackageAssignmentId { get; }
    public int RequestedByUserId { get; }
}
