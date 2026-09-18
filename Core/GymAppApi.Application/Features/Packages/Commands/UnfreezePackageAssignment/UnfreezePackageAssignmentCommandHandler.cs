using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;

public class UnfreezePackageAssignmentCommandHandler : IRequestHandler<UnfreezePackageAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UnfreezePackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UnfreezePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + self-servis: bkz. FreezePackageAssignmentCommandHandler'ın aynı yorumu.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Package),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Paket ataması {request.PackageAssignmentId} bulunamadı.");
        }

        bool callerIsAuthorized;
        if (assignment.MemberUserId == request.RequestedByUserId)
        {
            callerIsAuthorized = true;
        }
        else
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            callerIsAuthorized = callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        }
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paket atamasını aktifleştirme yetkiniz yok.");
        }

        var now = DateTime.UtcNow;
        if (assignment.EndDate is not null && assignment.FrozenAt is not null)
        {
            assignment.EndDate = assignment.EndDate.Value.Add(now - assignment.FrozenAt.Value);
        }
        assignment.Status = PackageAssignmentStatus.Active;
        assignment.FrozenAt = null;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
