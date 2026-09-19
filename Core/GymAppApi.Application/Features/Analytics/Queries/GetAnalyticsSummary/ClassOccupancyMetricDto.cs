namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public class ClassOccupancyMetricDto
{
    // 0..1 arası oran (0.75 = %75 doluluk). Son 30 günde hiç ClassSession
    // yoksa 0 döner (sıfıra bölme koruması).
    public decimal OverallOccupancyRate { get; set; }
    public IReadOnlyList<ClassOccupancyBreakdownDto> ClassBreakdown { get; set; } = Array.Empty<ClassOccupancyBreakdownDto>();
}
