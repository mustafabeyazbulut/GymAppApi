using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ContentLibrary.Queries.GetContentItems;

// Herkes çağırabilir ([Authorize]) - bir Member'ın ambient CompanyId'si yok,
// bu yüzden IgnoreQueryFilters + kendi görünürlük mantığımızı burada
// uyguluyoruz (GetClassSessionsQueryHandler'daki standing rule).
public class GetContentItemsQueryHandler : IRequestHandler<GetContentItemsQuery, IReadOnlyList<ContentItemDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetContentItemsQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ContentItemDto>> Handle(GetContentItemsQuery request, CancellationToken cancellationToken)
    {
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // Personel kapsamı, çağıranın herhangi bir (ör. ilk) ataması değil
        // AKTİF firmadaki ataması üzerinden belirlenir (ambient tenant
        // context, X-Active-Company-Id) - çok firmalı personel yanlış firmanın
        // içeriğini görmesin.
        var activeCompanyId = _tenantContext.CompanyId;
        var activeBranchId = _tenantContext.BranchId;
        var isStaffInActiveCompany = activeCompanyId != null && callerAssignments.Any(a =>
            a.CompanyId == activeCompanyId &&
            (a.Role == AssignmentRole.GymAdmin || a.Role == AssignmentRole.BranchManager || a.Role == AssignmentRole.Trainer));

        if (isStaffInActiveCompany)
        {
            // Staff (Trainer dahil) içerikleri IsActive filtresiz görür -
            // spec'in "staff tümünü görür" kuralı. GymAdmin aktif firmanın tüm şubelerini; şube kapsamlı personel
            // (BranchManager/Trainer) sadece kendi şubesini + şubesiz firma
            // içeriklerini (senaryo §10.8).
            var staffItems = await _unitOfWork.GetReadRepository<ContentItem>().GetAllAsync(
                c => c.CompanyId == activeCompanyId &&
                     (activeBranchId == null || c.BranchId == null || c.BranchId == activeBranchId),
                include: q => q.IgnoreQueryFilters().Include(c => c.MediaFile),
                orderBy: q => q.OrderByDescending(c => c.CreatedAt),
                cancellationToken: cancellationToken);

            return staffItems.Select(c => ToDto(c, hasAccess: true)).ToList();
        }

        // Member: sadece kendi GEÇERLİ paketlerinin (PackageAssignmentValidity
        // - süresi dolmuş ama Status'u Active kalmış paket sayılmaz) firma/
        // şubesindeki AKTİF içerikler: paketin şubesine ait içerik + şubesiz
        // firma içeriği (firma geneli pakette firmanın tümü). Premium içerik
        // de listede görünür (kilitli olarak), gerçek indirme kontrolü aynı
        // kuralla GET /api/media/{id}'de yapılır.
        var validPackageAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.UsableOwnedBy(request.RequestedByUserId, DateTime.UtcNow),
            include: q => q.IgnoreQueryFilters().Include(p => p.Package),
            cancellationToken: cancellationToken);

        if (validPackageAssignments.Count == 0)
        {
            return Array.Empty<ContentItemDto>();
        }

        var companyIds = validPackageAssignments.Select(p => p.CompanyId).Distinct().ToList();
        var candidateItems = await _unitOfWork.GetReadRepository<ContentItem>().GetAllAsync(
            c => c.IsActive && companyIds.Contains(c.CompanyId),
            include: q => q.IgnoreQueryFilters().Include(c => c.MediaFile),
            orderBy: q => q.OrderByDescending(c => c.CreatedAt),
            cancellationToken: cancellationToken);

        // Bir üyenin birden fazla firma/şubede geçerli paketi olabilir - her
        // içerik için, onu kapsayan paketlerin en yüksek erişim seviyesi esas.
        var result = new List<ContentItemDto>();
        foreach (var item in candidateItems)
        {
            var coveringPackages = validPackageAssignments
                .Where(p => p.CompanyId == item.CompanyId &&
                            (p.BranchId == null || item.BranchId == null || p.BranchId == item.BranchId))
                .ToList();
            if (coveringPackages.Count == 0)
            {
                continue;
            }

            var maxTier = coveringPackages.Max(p => p.Package!.AccessTier);
            result.Add(ToDto(item, hasAccess: item.RequiredAccessTier <= maxTier));
        }

        return result;
    }

    private static ContentItemDto ToDto(ContentItem c, bool hasAccess) => new()
    {
        Id = c.Id,
        CompanyId = c.CompanyId,
        BranchId = c.BranchId,
        Title = c.Title,
        Description = c.Description,
        RequiredAccessTier = c.RequiredAccessTier.ToString(),
        MediaFileId = c.MediaFileId,
        MediaContentType = c.MediaFile!.ContentType,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        HasAccess = hasAccess,
    };
}
