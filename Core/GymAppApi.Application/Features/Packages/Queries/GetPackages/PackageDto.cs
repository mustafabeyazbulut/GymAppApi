namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class PackageDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public decimal Price { get; set; }
    public string AccessTier { get; set; } = null!;
    public bool IsActive { get; set; }
}
