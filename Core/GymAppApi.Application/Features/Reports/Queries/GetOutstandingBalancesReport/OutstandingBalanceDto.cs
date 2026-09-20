namespace GymAppApi.Application.Features.Reports.Queries.GetOutstandingBalancesReport;

public class OutstandingBalanceDto
{
    public int PackageAssignmentId { get; set; }
    public string MemberFullName { get; set; } = null!;
    public string MemberPhone { get; set; } = null!;
    public string PackageName { get; set; } = null!;
    public string? BranchName { get; set; }
    public decimal Price { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal RemainingBalance { get; set; }
}
