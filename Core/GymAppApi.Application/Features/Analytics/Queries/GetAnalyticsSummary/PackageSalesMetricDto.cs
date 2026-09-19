namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public class PackageSalesMetricDto
{
    public int TotalCount { get; set; }
    public IReadOnlyList<PackageSalesCategoryBreakdownDto> CategoryBreakdown { get; set; } = Array.Empty<PackageSalesCategoryBreakdownDto>();
}
