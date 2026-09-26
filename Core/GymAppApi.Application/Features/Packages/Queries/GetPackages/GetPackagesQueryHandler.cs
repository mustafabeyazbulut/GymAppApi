using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

    // Paketleri yöneten roller (GymAdmin, BranchManager) pasif paketleri de
    // görür (IsActive alanıyla) - aksi hâlde pasife aldıkları paketi bulup
    // tekrar aktif yapamazlardı. Diğerleri sadece aktif paketleri görür.
    internal static bool CanSeeInactive(ITenantContext tenantContext) =>
        tenantContext.Role is AssignmentRole.GymAdmin or AssignmentRole.BranchManager;

    public async Task<IReadOnlyList<PackageDto>> Handle(GetPackagesQuery request, CancellationToken cancellationToken)
    {
        // Filtre pasifi gizlediği için IgnoreQueryFilters + firma kapsamı
        // açıkça (ambient firma yoksa hiçbir şey dönmez). Şube kapsamlı
        // personel (ambient BranchId set) sadece kendi şubesinin paketlerini
        // görür (senaryo §10.8). Firma geneli paket yok (§10.5): eski şubesiz
        // kayıtlar varsa sadece firma kapsamlı personel (GymAdmin) görür.
        var companyId = _tenantContext.CompanyId;
        var branchId = _tenantContext.BranchId;
        var includeInactive = CanSeeInactive(_tenantContext);
        var packages = await _unitOfWork.GetReadRepository<Package>().GetAllAsync(
            predicate: p => companyId != null && p.CompanyId == companyId &&
                            (branchId == null || p.BranchId == branchId) &&
                            (includeInactive || p.IsActive),
            include: q => q.IgnoreQueryFilters().Include(p => p.Company),
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
