namespace GymAppApi.Application.Features.Packages.Commands.RecordPackageAssignmentPayment;

public class RecordPackageAssignmentPaymentCommandResult
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal RemainingBalance { get; set; }
}
