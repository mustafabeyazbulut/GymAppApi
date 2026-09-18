using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentTrainers;

// Lets a Member (or staff) discover which Trainer ids they can pass into
// CreateReservationCommand.TrainerId for a given PackageAssignment - there is
// no general-purpose trainer directory endpoint, only this scoped one, since
// which trainers are bookable depends entirely on the assignment's own
// company/branch.
public class GetPackageAssignmentTrainersQueryHandler : IRequestHandler<GetPackageAssignmentTrainersQuery, IReadOnlyList<TrainerDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageAssignmentTrainersQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<TrainerDto>> Handle(GetPackageAssignmentTrainersQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Member-callable (booking their own package) -
        // same standing rule as the Payments/Reservations queries.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Company),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Paket ataması {request.PackageAssignmentId} bulunamadı.");
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
                throw new ForbiddenException("Bu paket ataması için antrenör listesini görme yetkiniz yok.");
            }
        }

        // assignment.BranchId == null means a company-wide package (see the
        // Package-scope decision in project-member-package-linkage-design.md)
        // - any trainer at any of the company's branches is bookable. A
        // branch-specific package only offers that one branch's trainers.
        var trainers = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.Role == AssignmentRole.Trainer && a.IsActive && a.CompanyId == assignment.CompanyId
                 && (assignment.BranchId == null || a.BranchId == assignment.BranchId),
            include: q => q.IgnoreQueryFilters().Include(a => a.User),
            cancellationToken: cancellationToken);

        return trainers
            .Select(a => new TrainerDto { Id = a.UserId, FullName = a.User!.FullName, BranchId = a.BranchId })
            .ToList();
    }
}
