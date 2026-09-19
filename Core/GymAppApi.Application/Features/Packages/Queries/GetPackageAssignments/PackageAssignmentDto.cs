namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignments;

public class PackageAssignmentDto
{
    public int Id { get; set; }
    public int PackageId { get; set; }
    public string PackageName { get; set; } = null!;
    public decimal Price { get; set; }
    public int MemberUserId { get; set; }
    public string MemberFullName { get; set; } = null!;
    public string MemberPhone { get; set; } = null!;
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? RemainingSessions { get; set; }
    public string Status { get; set; } = null!;
    public decimal TotalPaid { get; set; }
    public decimal RemainingBalance { get; set; }
    // null = dondurma süresi sınırsız.
    public int? MaxFreezeDays { get; set; }
    public int TotalFrozenDays { get; set; }
}
