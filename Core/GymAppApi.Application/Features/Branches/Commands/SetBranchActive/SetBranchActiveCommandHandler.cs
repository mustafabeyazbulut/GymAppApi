using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Branches.Commands.SetBranchActive;

public class SetBranchActiveCommandHandler : IRequestHandler<SetBranchActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetBranchActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetBranchActiveCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Branch'in global filtresi pasif şubeyi gizler -
        // filtreyle okunsaydı Gym Admin kapattığı şubeyi yeniden açamazdı
        // (senaryo §4.5: şube açar, düzenler, kapatır). Eskiden bunu sadece
        // filtreyi atlayan SuperAdmin yapabiliyordu; §10.6 ile o yol kapandı.
        // Güvenli: yetki aşağıda açıkça kontrol ediliyor.
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, include: q => q.IgnoreQueryFilters().Include(b => b.Company), cancellationToken: cancellationToken);

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // Çağıranın bu firmada hiç ataması yoksa şube "yok" sayılır (404) - başka
        // firmanın şubesinin varlığı sızdırılmaz.
        if (branch is null || !callerAssignments.Any(a => a.CompanyId == branch.CompanyId))
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
        }

        // Şube açmak/kapatmak bu firmanın Gym Admin'inin kararı; şubenin kendi
        // Şube Yöneticisinin değil.
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId);
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenSetBranchActive");
        }

        branch.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Branch>().Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
