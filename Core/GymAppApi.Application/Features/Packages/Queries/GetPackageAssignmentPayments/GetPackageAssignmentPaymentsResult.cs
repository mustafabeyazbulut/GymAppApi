namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentPayments;

public class PackageAssignmentPaymentDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = null!;
    public DateTime PaidAt { get; set; }
    public string? Note { get; set; }
}

public class GetPackageAssignmentPaymentsResult
{
    public IReadOnlyList<PackageAssignmentPaymentDto> Payments { get; set; } = new List<PackageAssignmentPaymentDto>();
    public decimal TotalPaid { get; set; }
    public decimal RemainingBalance { get; set; }
}
