using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;

public class GetBranchDetailQuery : IRequest<BranchListItemDto>
{
    public GetBranchDetailQuery(int branchId) => BranchId = branchId;

    public int BranchId { get; }
}
