using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordPackageAssignmentPayment;

public class RecordPackageAssignmentPaymentCommandHandler : IRequestHandler<RecordPackageAssignmentPaymentCommand, RecordPackageAssignmentPaymentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public RecordPackageAssignmentPaymentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<RecordPackageAssignmentPaymentCommandResult> Handle(RecordPackageAssignmentPaymentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        // Same authorization pattern as Freeze/Unfreeze/CancelPackageAssignmentCommandHandler.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenRecordPayment");
        }

        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == assignment.PackageId, cancellationToken: cancellationToken);
        var price = package?.Price ?? 0m;

        var existingPayments = await _unitOfWork.GetReadRepository<PackageAssignmentPayment>().GetAllAsync(
            p => p.PackageAssignmentId == assignment.Id, cancellationToken: cancellationToken);
        var alreadyPaid = existingPayments.Sum(p => p.Amount);

        var totalPaid = alreadyPaid + request.Amount;
        if (totalPaid > price)
        {
            throw new PaymentExceedsRemainingBalanceException();
        }

        var payment = new PackageAssignmentPayment
        {
            PackageAssignmentId = assignment.Id,
            CompanyId = assignment.CompanyId,
            Amount = request.Amount,
            Method = request.Method,
            PaidAt = DateTime.UtcNow,
            RecordedByUserId = request.RequestedByUserId,
            Note = request.Note,
        };
        await _unitOfWork.GetWriteRepository<PackageAssignmentPayment>().AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RecordPackageAssignmentPaymentCommandResult
        {
            Id = payment.Id,
            Amount = payment.Amount,
            TotalPaid = totalPaid,
            RemainingBalance = price - totalPaid,
        };
    }
}
