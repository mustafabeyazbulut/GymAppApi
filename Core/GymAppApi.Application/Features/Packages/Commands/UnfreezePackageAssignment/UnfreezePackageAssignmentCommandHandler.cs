using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Exceptions;
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
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
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
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        }
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenUnfreezePackageAssignment");
        }

        // Sadece Frozen bir atama açılabilir - bu kontrol olmadan bir üye
        // KENDİ İPTAL EDİLMİŞ (Cancelled) paketini unfreeze çağrısıyla
        // tekrar Active'e döndürebilirdi, kalıcı iptal garantisini
        // (bkz. CancelPackageAssignmentCommand) tamamen bypass ederek.
        if (assignment.Status != PackageAssignmentStatus.Frozen)
        {
            throw new PackageAssignmentNotFrozenException();
        }

        var now = DateTime.UtcNow;
        if (assignment.FrozenAt is not null)
        {
            var elapsedDays = (now - assignment.FrozenAt.Value).TotalDays;
            var maxFreezeDays = assignment.Package?.MaxFreezeDays;
            // Paketin bir dondurma sınırı varsa, EndDate'e eklenecek süre bu
            // döngüde kalan hakla sınırlanır - sınırsız süre dondurup
            // sonradan açarak sınırı aşmayı engeller (bkz.
            // FreezePackageAssignmentCommandHandler'ın giriş kontrolü).
            var appliedDays = maxFreezeDays is null
                ? elapsedDays
                : Math.Min(elapsedDays, Math.Max(0, maxFreezeDays.Value - assignment.TotalFrozenDays));

            if (assignment.EndDate is not null)
            {
                assignment.EndDate = assignment.EndDate.Value.AddDays(appliedDays);
            }
            // Kısmi bir gün her zaman tam gün olarak sayılır (yukarı
            // yuvarlama) - dondurma hakkını birkaç saatlik döngülere bölüp
            // sınırı aşmaya çalışmayı anlamsız kılar.
            assignment.TotalFrozenDays += (int)Math.Ceiling(appliedDays);
        }
        assignment.Status = PackageAssignmentStatus.Active;
        assignment.FrozenAt = null;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
