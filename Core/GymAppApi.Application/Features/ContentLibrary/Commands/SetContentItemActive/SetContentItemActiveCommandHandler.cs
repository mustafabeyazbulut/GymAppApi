using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ContentLibrary.Commands.SetContentItemActive;

public class SetContentItemActiveCommandHandler : IRequestHandler<SetContentItemActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public SetContentItemActiveCommandHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task Handle(SetContentItemActiveCommand request, CancellationToken cancellationToken)
    {
        // Filtresiz okuma: global filtre pasif içeriği ve platform içeriğini
        // gizler - filtreli okumada pasif bir içerik tekrar aktif yapılamazdı
        // (SetBranchActive'deki aynı düzeltme). Görünürlük aşağıda açıkça
        // kontrol edilir.
        var contentItem = await _unitOfWork.GetReadRepository<ContentItem>().GetAsync(
            c => c.Id == request.ContentItemId,
            include: q => q.IgnoreQueryFilters().Include(c => c.MediaFile),
            enableTracking: true,
            cancellationToken: cancellationToken);

        if (contentItem is not null && contentItem.CompanyId is null)
        {
            // Genel (platform) içerik herkese görünür; onu sadece Sistem Sahibi yönetir.
            if (_tenantContext.Role != AssignmentRole.SuperAdmin)
            {
                throw new ForbiddenException("ForbiddenSetContentItemActive");
            }
        }
        else
        {
            // Gym içeriği: başka firmanın (veya firma bağlamı olmayan
            // çağıranın) içeriği "yok" gibi davranır.
            if (contentItem is null || _tenantContext.CompanyId is null || contentItem.CompanyId != _tenantContext.CompanyId)
            {
                throw new NotFoundException("ContentItemNotFound", request.ContentItemId);
            }

            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            var callerIsAuthorized = callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == contentItem.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == contentItem.BranchId));
            if (!callerIsAuthorized)
            {
                throw new ForbiddenException("ForbiddenSetContentItemActive");
            }
        }

        contentItem.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<ContentItem>().Update(contentItem);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
