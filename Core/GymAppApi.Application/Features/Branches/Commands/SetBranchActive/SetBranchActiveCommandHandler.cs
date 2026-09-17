using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.SetBranchActive;

public class SetBranchActiveCommandHandler : IRequestHandler<SetBranchActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetBranchActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetBranchActiveCommand request, CancellationToken cancellationToken)
    {
        // Branch is ICompanyScoped, IDeactivatable: its global query filter hides an
        // inactive branch from every non-SuperAdmin caller. This means a GymAdmin who
        // deactivates their own branch will get NotFoundException here if they try to
        // reactivate it themselves - only SuperAdmin (who bypasses the filter) can
        // successfully call this to flip it back. Intentional, matches how Company
        // deactivation already behaves, not a bug.
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        // Same re-check pattern as CreateBranchCommandHandler - opening or
        // closing a branch is a GymAdmin(of this company)/SuperAdmin
        // decision, not the branch's own BranchManager's call.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeyi aktif/pasif yapma yetkiniz yok.");
        }

        branch.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Branch>().Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
