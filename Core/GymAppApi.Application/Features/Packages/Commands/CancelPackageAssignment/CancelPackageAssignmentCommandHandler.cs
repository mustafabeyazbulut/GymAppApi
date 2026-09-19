using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;

public class CancelPackageAssignmentCommandHandler : IRequestHandler<CancelPackageAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public CancelPackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(CancelPackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCancelPackageAssignment");
        }

        assignment.Status = PackageAssignmentStatus.Cancelled;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
