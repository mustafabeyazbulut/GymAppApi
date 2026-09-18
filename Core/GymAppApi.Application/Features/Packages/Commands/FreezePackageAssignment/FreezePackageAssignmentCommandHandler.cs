using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;

public class FreezePackageAssignmentCommandHandler : IRequestHandler<FreezePackageAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public FreezePackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(FreezePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Member-erişilebilir (kendi üyeliğini dondurma) -
        // Payments/Reservations/CheckIns/ProgressNotes sorgularıyla aynı
        // standing rule. Gerçek dünya gereksinimi: bir üyenin "seyahate
        // çıkıyorum, üyeliğimi dondur" demesi için personeli beklemesi
        // gerekmemeli - dondurma/açma geri alınabilir ve düşük riskli, bu
        // yüzden self-servis; Cancel (kalıcı iptal) hâlâ staff-only kalıyor.
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
            throw new ForbiddenException("Bu paket atamasını dondurma yetkiniz yok.");
        }

        assignment.Status = PackageAssignmentStatus.Frozen;
        assignment.FrozenAt = DateTime.UtcNow;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
