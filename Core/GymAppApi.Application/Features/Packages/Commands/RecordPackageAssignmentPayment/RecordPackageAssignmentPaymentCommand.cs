using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordPackageAssignmentPayment;

public class RecordPackageAssignmentPaymentCommand : IRequest<RecordPackageAssignmentPaymentCommandResult>
{
    // Set by the controller from the route segment.
    public int PackageAssignmentId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Note { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
