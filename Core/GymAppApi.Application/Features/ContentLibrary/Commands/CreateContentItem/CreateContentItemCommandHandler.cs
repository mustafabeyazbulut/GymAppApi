using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;

public class CreateContentItemCommandHandler : IRequestHandler<CreateContentItemCommand, CreateContentItemCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaStorage _mediaStorage;

    public CreateContentItemCommandHandler(IUnitOfWork unitOfWork, IMediaStorage mediaStorage)
    {
        _unitOfWork = unitOfWork;
        _mediaStorage = mediaStorage;
    }

    public async Task<CreateContentItemCommandResult> Handle(CreateContentItemCommand request, CancellationToken cancellationToken)
    {
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        int companyId;
        if (request.BranchId is not null)
        {
            var branch = await _unitOfWork.GetReadRepository<Branch>()
                .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
            if (branch is null)
            {
                throw new NotFoundException("BranchNotFound", request.BranchId.Value);
            }

            var callerIsAuthorized = callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == request.BranchId));
            if (!callerIsAuthorized)
            {
                throw new ForbiddenException("ForbiddenCreateContentItem");
            }

            companyId = branch.CompanyId;
        }
        else
        {
            // BranchId verilmemişse "firmanın tüm şubelerinde görünür" bir
            // içerik - bu sadece GymAdmin'in kendi firması için verebileceği
            // bir karar (bkz. CreatePackageCommandHandler'ın aynı deseni).
            // SuperAdmin'in bir Assignment.CompanyId'si olmadığı için bu dalda
            // kasıtlı olarak yer almıyor - hangi firma için yükleme yaptığı
            // belirsiz kalırdı.
            var gymAdminAssignment = callerAssignments.FirstOrDefault(a => a.Role == AssignmentRole.GymAdmin);
            if (gymAdminAssignment is null)
            {
                throw new ForbiddenException("ForbiddenCreateContentItem");
            }

            companyId = gymAdminAssignment.CompanyId!.Value;
        }

        var storagePath = await _mediaStorage.SaveAsync(request.FileContent, request.FileContentType, cancellationToken);
        var mediaFile = new MediaFile
        {
            StoragePath = storagePath,
            ContentType = request.FileContentType,
            SizeBytes = request.FileContent.Length,
            UploadedByUserId = request.RequestedByUserId,
        };
        await _unitOfWork.GetWriteRepository<MediaFile>().AddAsync(mediaFile, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var contentItem = new ContentItem
        {
            CompanyId = companyId,
            BranchId = request.BranchId,
            Title = request.Title,
            Description = request.Description,
            RequiredAccessTier = request.RequiredAccessTier,
            MediaFileId = mediaFile.Id,
            CreatedByUserId = request.RequestedByUserId,
        };
        await _unitOfWork.GetWriteRepository<ContentItem>().AddAsync(contentItem, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateContentItemCommandResult { Id = contentItem.Id, Title = contentItem.Title, MediaFileId = mediaFile.Id };
    }
}
