using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class GetBranchesQueryHandler : IRequestHandler<GetBranchesQuery, IReadOnlyList<BranchListItemDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetBranchesQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<BranchListItemDto>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        // Global query filter sadece firmaya göre daraltıyor - şube kapsamlı
        // personel (BranchManager/Trainer, ambient BranchId set) sadece kendi
        // şubesini görür (senaryo §10.8). GetRevenueReportQueryHandler'ın
        // aynı _tenantContext.BranchId deseni.
        var branchId = _tenantContext.BranchId;
        var branches = await _unitOfWork.GetReadRepository<Branch>().GetAllAsync(
            predicate: b => branchId == null || b.Id == branchId,
            cancellationToken: cancellationToken);

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
