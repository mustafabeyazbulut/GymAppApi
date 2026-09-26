using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.SetPackageActive;

public class SetPackageActiveCommandHandler : IRequestHandler<SetPackageActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetPackageActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetPackageActiveCommand request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException("PackageNotFound", request.PackageId);
        }

        // Same GymAdmin(of company)-only rule as SetBranchActiveCommandHandler -
        // a BranchManager may not retire/reactivate even their own branch's package.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManagePackage");
        }

        package.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Package>().Update(package);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
