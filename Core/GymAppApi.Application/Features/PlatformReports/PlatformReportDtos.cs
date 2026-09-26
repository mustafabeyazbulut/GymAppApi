namespace GymAppApi.Application.Features.PlatformReports;

public class PlatformSummaryDto
{
    public int TotalUsers { get; set; }
    public int NewUsersInPeriod { get; set; }
    public List<UserGrowthPointDto> UserGrowth { get; set; } = new();
    public int CompanyCount { get; set; }
    public int ActiveCompanyCount { get; set; }
    // Sadece aktif şubeler.
    public int BranchCount { get; set; }
    public int ActiveMemberCount { get; set; }
    public int TrainerCount { get; set; }
    public int StaffCount { get; set; }
    public int PackageSalesInPeriod { get; set; }
    public decimal RevenueInPeriod { get; set; }
    public string Currency { get; set; } = "TRY";
    public List<PlatformCompanyReportDto> Companies { get; set; } = new();
}

public class UserGrowthPointDto
{
    public DateOnly Date { get; set; }
    public int NewUsers { get; set; }
}

public class PlatformCompanyReportDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = null!;
    public bool IsActive { get; set; }
    public int BranchCount { get; set; }
    public int ActiveMemberCount { get; set; }
    public int TrainerCount { get; set; }
    public int StaffCount { get; set; }
    public int PackageSalesInPeriod { get; set; }
    public decimal RevenueInPeriod { get; set; }
}

public class PlatformBranchReportDto
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public bool IsActive { get; set; }
    public int ActiveMemberCount { get; set; }
    public int TrainerCount { get; set; }
    // Şube düzeyinde personel = şubenin Şube Yöneticileri (Gym Admin firma geneli).
    public int StaffCount { get; set; }
    public int PackageSalesInPeriod { get; set; }
    public decimal RevenueInPeriod { get; set; }
}
