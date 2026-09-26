using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Commands.SetPackageActive;

public class SetPackageActiveCommandHandler : IRequestHandler<SetPackageActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public SetPackageActiveCommandHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task Handle(SetPackageActiveCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: global filtre pasif paketi gizler - filtreyle
        // okunsaydı pasif paket tekrar aktif yapılamazdı (SetBranchActive'deki
        // aynı düzeltme). Görünürlük ve yetki aşağıda aktif bağlama göre
        // açıkça kontrol ediliyor.
        var package = await _unitOfWork.GetReadRepository<Package>().GetAsync(
            p => p.Id == request.PackageId,
            include: q => q.IgnoreQueryFilters().Include(p => p.Company),
            enableTracking: true,
            cancellationToken: cancellationToken);

        // Başka firmanın / (şube kapsamlı personel için) başka şubenin paketi
        // "yok" sayılır - GetPackageDetail'in aynı kuralı, varlığı sızmaz.
        var companyId = _tenantContext.CompanyId;
        var branchId = _tenantContext.BranchId;
        if (package is null || companyId is null || package.CompanyId != companyId ||
            (branchId != null && package.BranchId != branchId))
        {
            throw new NotFoundException("PackageNotFound", request.PackageId);
        }

        // Senaryo §4.4: GymAdmin firmanın tüm paketlerini, Şube Yöneticisi
        // sadece kendi şubesinin paketlerini aktif/pasif yapar.
        var callerIsAuthorized = _tenantContext.Role switch
        {
            AssignmentRole.GymAdmin => true,
            AssignmentRole.BranchManager => branchId != null && package.BranchId == branchId,
            _ => false,
        };
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManagePackage");
        }

        package.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Package>().Update(package);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
