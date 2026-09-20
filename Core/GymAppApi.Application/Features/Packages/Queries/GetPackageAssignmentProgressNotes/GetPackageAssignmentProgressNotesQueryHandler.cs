using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentProgressNotes;

public class GetPackageAssignmentProgressNotesQueryHandler : IRequestHandler<GetPackageAssignmentProgressNotesQuery, IReadOnlyList<ProgressNoteDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageAssignmentProgressNotesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ProgressNoteDto>> Handle(GetPackageAssignmentProgressNotesQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters (burada ve aşağıda): Member-erişilebilir (kendi
        // ilerleme geçmişi) - Payments/Reservations/CheckIns sorgularıyla
        // aynı standing rule.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Package),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        if (assignment.MemberUserId != request.RequestedByUserId)
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            var callerIsAuthorized = callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId) ||
                (a.Role == AssignmentRole.Trainer && a.BranchId == assignment.BranchId));
            if (!callerIsAuthorized)
            {
                throw new ForbiddenException("ForbiddenViewProgressNotes");
            }
        }

        var notes = await _unitOfWork.GetReadRepository<ProgressNote>().GetAllAsync(
            n => n.PackageAssignmentId == assignment.Id,
            include: q => q.IgnoreQueryFilters().Include(n => n.PackageAssignment),
            cancellationToken: cancellationToken);

        return notes
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ProgressNoteDto
            {
                Id = n.Id,
                TechniqueScore = n.TechniqueScore,
                ConditionScore = n.ConditionScore,
                NoteText = n.NoteText,
                CreatedAt = n.CreatedAt,
                MediaFileId = n.MediaFileId,
            })
            .ToList();
    }
}
