using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Media.Queries.GetMediaFile;

// URL'yi bilen ama yetkisi olmayan biri dosyayı çekemesin diye ContentItem'daki
// (ve Gelişim Takibi medyası eklenince ProgressNote'daki) erişim kuralı burada
// TEKRAR kontrol edilir - bkz. docs/superpowers/specs/2026-09-20-content-library-design.md.
public class GetMediaFileQueryHandler : IRequestHandler<GetMediaFileQuery, GetMediaFileResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaStorage _mediaStorage;

    public GetMediaFileQueryHandler(IUnitOfWork unitOfWork, IMediaStorage mediaStorage)
    {
        _unitOfWork = unitOfWork;
        _mediaStorage = mediaStorage;
    }

    public async Task<GetMediaFileResult> Handle(GetMediaFileQuery request, CancellationToken cancellationToken)
    {
        var mediaFile = await _unitOfWork.GetReadRepository<MediaFile>()
            .GetAsync(m => m.Id == request.MediaFileId, cancellationToken: cancellationToken);
        if (mediaFile is null)
        {
            throw new NotFoundException("MediaFileNotFound", request.MediaFileId);
        }

        var contentItem = await _unitOfWork.GetReadRepository<ContentItem>().GetAsync(
            c => c.MediaFileId == request.MediaFileId,
            include: q => q.IgnoreQueryFilters().Include(c => c.Company),
            cancellationToken: cancellationToken);
        if (contentItem is not null)
        {
            await EnsureCanViewContentItemAsync(contentItem, request.RequestedByUserId, cancellationToken);
        }
        else
        {
            var progressNote = await _unitOfWork.GetReadRepository<ProgressNote>().GetAsync(
                n => n.MediaFileId == request.MediaFileId,
                include: q => q.IgnoreQueryFilters().Include(n => n.PackageAssignment),
                cancellationToken: cancellationToken);
            if (progressNote is null)
            {
                throw new NotFoundException("MediaFileNotFound", request.MediaFileId);
            }

            await EnsureCanViewProgressNoteAsync(progressNote, request.RequestedByUserId, cancellationToken);
        }

        var content = await _mediaStorage.OpenReadAsync(mediaFile.StoragePath, cancellationToken);
        return new GetMediaFileResult { Content = content, ContentType = mediaFile.ContentType };
    }

    private async Task EnsureCanViewContentItemAsync(ContentItem contentItem, int requestedByUserId, CancellationToken cancellationToken)
    {
        // Şube kuralı GetContentItemsQueryHandler ile aynı (senaryo §10.8):
        // şubesiz (BranchId null) içerik firmanın tümüne açık; şubeye ait
        // içerik sadece o şubenin personeline / o şubede geçerli paketi olan
        // üyeye (veya firma geneli pakete) açık. GymAdmin'in ataması şubesiz
        // olduğu için firmanın tüm içeriğini görür.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == requestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var isStaffForContent = callerAssignments.Any(a =>
            ((a.Role == AssignmentRole.GymAdmin || a.Role == AssignmentRole.BranchManager || a.Role == AssignmentRole.Trainer) &&
             a.CompanyId == contentItem.CompanyId &&
             (a.BranchId == null || contentItem.BranchId == null || a.BranchId == contentItem.BranchId)));
        if (isStaffForContent)
        {
            return;
        }

        // Sadece GEÇERLİ paketler (PackageAssignmentValidity) - süresi dolmuş
        // ama Status'u hâlâ Active olan bir paket içerik erişimi vermez.
        var validPackageAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.UsableOwnedBy(requestedByUserId, DateTime.UtcNow),
            include: q => q.IgnoreQueryFilters().Include(p => p.Package),
            cancellationToken: cancellationToken);

        var hasAccess = validPackageAssignments.Any(p =>
            p.CompanyId == contentItem.CompanyId &&
            (p.BranchId == null || contentItem.BranchId == null || p.BranchId == contentItem.BranchId) &&
            p.Package!.AccessTier >= contentItem.RequiredAccessTier);
        if (!hasAccess)
        {
            throw new ForbiddenException("ForbiddenViewMedia");
        }
    }

    // GetPackageAssignmentProgressNotesQueryHandler'ın aynı yetki kontrolü -
    // notun ait olduğu üye ya da o notu görebilen personel (antrenörün
    // kendisi/GymAdmin/BranchManager).
    private async Task EnsureCanViewProgressNoteAsync(ProgressNote progressNote, int requestedByUserId, CancellationToken cancellationToken)
    {
        if (progressNote.PackageAssignment!.MemberUserId == requestedByUserId)
        {
            return;
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == requestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == progressNote.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == progressNote.BranchId) ||
            (a.Role == AssignmentRole.Trainer && a.BranchId == progressNote.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenViewMedia");
        }
    }
}
