using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;

public class GetBranchDetailQueryHandler : IRequestHandler<GetBranchDetailQuery, BranchListItemDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetBranchDetailQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<BranchListItemDto> Handle(GetBranchDetailQuery request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        // Şube kapsamlı personel için aynı firmanın başka bir şubesi de
        // "yok" sayılır - 403 değil 404, varlığı sızdırılmasın (başka
        // firmanın şubesine global filtrenin zaten verdiği yanıtla aynı).
        if (branch is null || (_tenantContext.BranchId != null && branch.Id != _tenantContext.BranchId))
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
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
