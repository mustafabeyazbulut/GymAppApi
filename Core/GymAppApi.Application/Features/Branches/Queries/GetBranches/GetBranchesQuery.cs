using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class GetBranchesQuery : IRequest<IReadOnlyList<BranchListItemDto>>
{
}
