namespace GymAppApi.Application.Features.Reports.Queries.GetRevenueReport;

public class RevenueReportDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal TotalAmount { get; set; }
    public IReadOnlyList<RevenueMethodBreakdownDto> MethodBreakdown { get; set; } = Array.Empty<RevenueMethodBreakdownDto>();
    // Tarihe göre artan sırada - mobilin basit bir çubuk grafik çizebilmesi için.
    public IReadOnlyList<RevenueDailyBreakdownDto> DailyBreakdown { get; set; } = Array.Empty<RevenueDailyBreakdownDto>();
}

public class RevenueMethodBreakdownDto
{
    public string Method { get; set; } = null!;
    public decimal Amount { get; set; }
}

public class RevenueDailyBreakdownDto
{
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
}
