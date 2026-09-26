using GymAppApi.Application.Common.Interfaces;
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

        var isSuperAdmin = callerAssignments.Any(a => a.Role == AssignmentRole.SuperAdmin);
        // Personel kapsamı, çağıranın herhangi bir (ör. ilk) ataması değil
        // AKTİF firmadaki ataması üzerinden belirlenir (ambient tenant
        // context, X-Active-Company-Id) - çok firmalı personel yanlış firmanın
        // içeriğini görmesin.
        var activeCompanyId = _tenantContext.CompanyId;
        var activeBranchId = _tenantContext.BranchId;
        var isStaffInActiveCompany = activeCompanyId != null && callerAssignments.Any(a =>
            a.CompanyId == activeCompanyId &&
            (a.Role == AssignmentRole.GymAdmin || a.Role == AssignmentRole.BranchManager || a.Role == AssignmentRole.Trainer));

        if (isSuperAdmin || isStaffInActiveCompany)
        {
            // Staff (Trainer dahil) içerikleri IsActive filtresiz görür -
            // spec'in "staff tümünü görür" kuralı. SuperAdmin tüm firmaları;
            // GymAdmin aktif firmanın tüm şubelerini; şube kapsamlı personel
            // (BranchManager/Trainer) sadece kendi şubesini + şubesiz firma
            // içeriklerini (senaryo §10.8).
            var staffItems = await _unitOfWork.GetReadRepository<ContentItem>().GetAllAsync(
                c => isSuperAdmin ||
                     (c.CompanyId == activeCompanyId &&
                      (activeBranchId == null || c.BranchId == null || c.BranchId == activeBranchId)),
                include: q => q.IgnoreQueryFilters().Include(c => c.MediaFile),
                orderBy: q => q.OrderByDescending(c => c.CreatedAt),
                cancellationToken: cancellationToken);

            return staffItems.Select(c => ToDto(c, hasAccess: true)).ToList();
        }

        // Member: sadece kendi aktif PackageAssignment'ı olan firmaların
        // AKTİF içerikleri - ama Premium içerik de listede görünür (kilitli
        // olarak), gerçek indirme kontrolü GET /api/media/{id}'de yapılır.
        var memberAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            p => p.MemberUserId == request.RequestedByUserId && p.Status == PackageAssignmentStatus.Active,
            include: q => q.IgnoreQueryFilters().Include(p => p.Package),
            cancellationToken: cancellationToken);

        if (memberAssignments.Count == 0)
        {
            return Array.Empty<ContentItemDto>();
        }

        // Bir üyenin birden fazla firmada aktif üyeliği olabilir - her firma
        // için ayrı en yüksek erişim seviyesi hesaplanır.
        var maxTierByCompany = memberAssignments
            .GroupBy(p => p.CompanyId)
            .ToDictionary(g => g.Key, g => g.Max(p => p.Package!.AccessTier));

        var memberItems = await _unitOfWork.GetReadRepository<ContentItem>().GetAllAsync(
            c => c.IsActive && maxTierByCompany.Keys.Contains(c.CompanyId),
            include: q => q.IgnoreQueryFilters().Include(c => c.MediaFile),
            orderBy: q => q.OrderByDescending(c => c.CreatedAt),
            cancellationToken: cancellationToken);

        return memberItems
            .Select(c => ToDto(c, hasAccess: c.RequiredAccessTier <= maxTierByCompany[c.CompanyId]))
            .ToList();
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
