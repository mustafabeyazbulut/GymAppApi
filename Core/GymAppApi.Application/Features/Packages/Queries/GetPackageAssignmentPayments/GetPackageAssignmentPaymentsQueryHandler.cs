using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentPayments;

public class GetPackageAssignmentPaymentsQueryHandler : IRequestHandler<GetPackageAssignmentPaymentsQuery, GetPackageAssignmentPaymentsResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageAssignmentPaymentsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<GetPackageAssignmentPaymentsResult> Handle(GetPackageAssignmentPaymentsQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters (here and below): unlike the staff-only endpoints
        // in this feature, this one is also callable by the assignment's own
        // Member - who typically has no Assignment row at all, so their
        // ambient CompanyId is always null (see TenantResolutionService) and
        // the ICompanyScoped filter on PackageAssignment/Package/
        // PackageAssignmentPayment would otherwise hide their own data from
        // themselves. Safe because the explicit authorization check below
        // (staff-of-this-company/branch OR MemberUserId == caller) is what
        // actually gates access here, not the tenant filter - same rationale
        // as GetMeQueryHandler. Also includes Package to get its Price in the
        // same bypassed query, since a separate filtered Package lookup would
        // hit the identical problem.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Package),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        // Same staff authorization pattern as Freeze/Unfreeze/Cancel, plus the
        // assignment's own Member can always see their own payment history.
        if (assignment.MemberUserId != request.RequestedByUserId)
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            var callerIsAuthorized = callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
            if (!callerIsAuthorized)
            {
                throw new ForbiddenException("ForbiddenPackageAssignmentPayments");
            }
        }

        var price = assignment.Package?.Price ?? 0m;

        var payments = await _unitOfWork.GetReadRepository<PackageAssignmentPayment>().GetAllAsync(
            p => p.PackageAssignmentId == assignment.Id,
            include: q => q.IgnoreQueryFilters().Include(p => p.PackageAssignment),
            cancellationToken: cancellationToken);
        var totalPaid = payments.Sum(p => p.Amount);

        return new GetPackageAssignmentPaymentsResult
        {
            Payments = payments
                .Select(p => new PackageAssignmentPaymentDto
                {
                    Id = p.Id,
                    Amount = p.Amount,
                    Method = p.Method.ToString(),
                    PaidAt = p.PaidAt,
                    Note = p.Note,
                })
                .ToList(),
            TotalPaid = totalPaid,
            RemainingBalance = price - totalPaid,
        };
    }
}
