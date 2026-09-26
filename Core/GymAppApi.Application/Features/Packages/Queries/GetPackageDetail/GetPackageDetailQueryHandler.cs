using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
        // GetPackagesQueryHandler'ın kuralları: filtresiz okuma + açık firma
        // kapsamı; pasif paketi sadece yöneticiler görür; şube kapsamlı
        // personel için başka şubenin paketi "yok" sayılır (403 değil 404,
        // varlığı sızdırılmasın).
        var package = await _unitOfWork.GetReadRepository<Package>().GetAsync(
            p => p.Id == request.PackageId,
            include: q => q.IgnoreQueryFilters().Include(p => p.Company),
            cancellationToken: cancellationToken);
        var companyId = _tenantContext.CompanyId;
        var branchId = _tenantContext.BranchId;
        if (package is null || companyId is null || package.CompanyId != companyId ||
            (branchId != null && package.BranchId != branchId) ||
            (!package.IsActive && !GetPackagesQueryHandler.CanSeeInactive(_tenantContext)))
        {
            throw new NotFoundException("PackageNotFound", request.PackageId);
        }

        return GetPackagesQueryHandler.ToDto(package);
    }
}
