using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

public class RecordProgressNoteCommandHandler : IRequestHandler<RecordProgressNoteCommand, RecordProgressNoteCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaStorage _mediaStorage;

    public RecordProgressNoteCommandHandler(IUnitOfWork unitOfWork, IMediaStorage mediaStorage)
    {
        _unitOfWork = unitOfWork;
        _mediaStorage = mediaStorage;
    }

    public async Task<RecordProgressNoteCommandResult> Handle(RecordProgressNoteCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        // Trainer da dahil - GetPackageAssignmentReservations/CheckIns'le aynı
        // şube-eşleşmeli kontrol: bu şubede çalışan bir antrenör, gerçek
        // dünyada birlikte çalıştığı bir üye için ilerleme notu bırakabilmeli.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId) ||
            (a.Role == AssignmentRole.Trainer && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenRecordProgressNote");
        }

        int? mediaFileId = null;
        if (request.MediaFileContent is not null)
        {
            var storagePath = await _mediaStorage.SaveAsync(request.MediaFileContent, request.MediaFileContentType!, cancellationToken);
            var mediaFile = new MediaFile
            {
                StoragePath = storagePath,
                ContentType = request.MediaFileContentType!,
                SizeBytes = request.MediaFileContent.Length,
                UploadedByUserId = request.RequestedByUserId,
            };
            await _unitOfWork.GetWriteRepository<MediaFile>().AddAsync(mediaFile, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            mediaFileId = mediaFile.Id;
        }

        var note = new ProgressNote
        {
            PackageAssignmentId = assignment.Id,
            CompanyId = assignment.CompanyId,
            BranchId = assignment.BranchId,
            RecordedByUserId = request.RequestedByUserId,
            TechniqueScore = request.TechniqueScore,
            ConditionScore = request.ConditionScore,
            NoteText = request.NoteText,
            MediaFileId = mediaFileId,
        };
        await _unitOfWork.GetWriteRepository<ProgressNote>().AddAsync(note, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RecordProgressNoteCommandResult
        {
            Id = note.Id,
            TechniqueScore = note.TechniqueScore,
            ConditionScore = note.ConditionScore,
            NoteText = note.NoteText,
            CreatedAt = note.CreatedAt,
            MediaFileId = note.MediaFileId,
        };
    }
}
