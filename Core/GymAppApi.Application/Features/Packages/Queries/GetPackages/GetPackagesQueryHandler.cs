using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class GetPackagesQueryHandler : IRequestHandler<GetPackagesQuery, IReadOnlyList<PackageDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetPackagesQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<PackageDto>> Handle(GetPackagesQuery request, CancellationToken cancellationToken)
    {
        // Global query filter sadece firmaya göre daraltıyor - şube kapsamlı
        // personel (ambient BranchId set) sadece kendi şubesinin paketlerini
        // görür (senaryo §10.8). Firma geneli paket yok (§10.5): eski şubesiz
        // kayıtlar varsa sadece firma kapsamlı personel (GymAdmin) görür.
        var branchId = _tenantContext.BranchId;
        var packages = await _unitOfWork.GetReadRepository<Package>().GetAllAsync(
            predicate: p => branchId == null || p.BranchId == branchId,
            cancellationToken: cancellationToken);

        return packages.Select(ToDto).ToList();
    }

    internal static PackageDto ToDto(Package p) => new()
    {
        Id = p.Id,
        CompanyId = p.CompanyId,
        BranchId = p.BranchId,
        Name = p.Name,
        Description = p.Description,
        Type = p.Type.ToString(),
        DurationDays = p.DurationDays,
        SessionCount = p.SessionCount,
        Price = p.Price,
        AccessTier = p.AccessTier.ToString(),
        IsActive = p.IsActive,
        MaxFreezeDays = p.MaxFreezeDays,
    };
}
