using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
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
            // Şimdilik tek olası sahip ContentItem - Gelişim Takibi medyası
            // eklendiğinde buraya bir ProgressNote kontrolü de eklenecek.
            throw new NotFoundException("MediaFileNotFound", request.MediaFileId);
        }

        var content = await _mediaStorage.OpenReadAsync(mediaFile.StoragePath, cancellationToken);
        return new GetMediaFileResult { Content = content, ContentType = mediaFile.ContentType };
    }

    private async Task EnsureCanViewContentItemAsync(ContentItem contentItem, int requestedByUserId, CancellationToken cancellationToken)
    {
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == requestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var isStaffOfCompany = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            ((a.Role == AssignmentRole.GymAdmin || a.Role == AssignmentRole.BranchManager || a.Role == AssignmentRole.Trainer) &&
             a.CompanyId == contentItem.CompanyId));
        if (isStaffOfCompany)
        {
            return;
        }

        var memberAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            p => p.MemberUserId == requestedByUserId &&
                 p.CompanyId == contentItem.CompanyId &&
                 p.Status == PackageAssignmentStatus.Active,
            include: q => q.IgnoreQueryFilters().Include(p => p.Package),
            cancellationToken: cancellationToken);

        var hasAccess = memberAssignments.Any(p => p.Package!.AccessTier >= contentItem.RequiredAccessTier);
        if (!hasAccess)
        {
            throw new ForbiddenException("ForbiddenViewMedia");
        }
    }
}
