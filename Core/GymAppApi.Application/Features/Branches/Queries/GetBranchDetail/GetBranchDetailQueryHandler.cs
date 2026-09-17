using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;

public class GetBranchDetailQueryHandler : IRequestHandler<GetBranchDetailQuery, BranchListItemDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBranchDetailQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<BranchListItemDto> Handle(GetBranchDetailQuery request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        return new BranchListItemDto
        {
            Id = branch.Id,
            CompanyId = branch.CompanyId,
            Name = branch.Name,
            Address = branch.Address,
            IsActive = branch.IsActive,
        };
    }
}
