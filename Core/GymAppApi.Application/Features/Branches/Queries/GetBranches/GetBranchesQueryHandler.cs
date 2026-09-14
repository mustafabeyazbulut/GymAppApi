using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class GetBranchesQueryHandler : IRequestHandler<GetBranchesQuery, IReadOnlyList<BranchListItemDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBranchesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<BranchListItemDto>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        var branches = await _unitOfWork.GetReadRepository<Branch>().GetAllAsync(cancellationToken: cancellationToken);

        return branches.Select(b => new BranchListItemDto
        {
            Id = b.Id,
            CompanyId = b.CompanyId,
            Name = b.Name,
            Address = b.Address,
            IsActive = b.IsActive
        }).ToList();
    }
}
