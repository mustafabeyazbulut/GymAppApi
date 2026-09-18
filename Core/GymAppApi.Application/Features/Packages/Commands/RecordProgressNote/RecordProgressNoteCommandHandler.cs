using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;

public class RecordProgressNoteCommandHandler : IRequestHandler<RecordProgressNoteCommand, RecordProgressNoteCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public RecordProgressNoteCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<RecordProgressNoteCommandResult> Handle(RecordProgressNoteCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Paket ataması {request.PackageAssignmentId} bulunamadı.");
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
            throw new ForbiddenException("Bu paket ataması için ilerleme notu bırakma yetkiniz yok.");
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
        };
    }
}
