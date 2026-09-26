using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;

public class GetPackageDetailQueryHandler : IRequestHandler<GetPackageDetailQuery, PackageDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetPackageDetailQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PackageDto> Handle(GetPackageDetailQuery request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        // GetPackagesQueryHandler'ın şube kuralı: şube kapsamlı personel için
        // başka şubenin paketi "yok" sayılır (403 değil 404, varlığı sızdırılmasın).
        var branchId = _tenantContext.BranchId;
        if (package is null || (branchId != null && package.BranchId != branchId))
        {
            throw new NotFoundException("PackageNotFound", request.PackageId);
        }

        return GetPackagesQueryHandler.ToDto(package);
    }
}
