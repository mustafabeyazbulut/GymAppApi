namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public class ActiveMemberMetricDto
{
    public int CurrentCount { get; set; }
    public int CountThirtyDaysAgo { get; set; }

    // Yüzde olarak trend (ör. 12.5 = %12.5 artış, -8 = %8 düşüş). 30 gün
    // önce hiç aktif üye yoksa (CountThirtyDaysAgo == 0) sıfıra bölme
    // olacağından null döner - frontend bunu "yeni" / kıyaslanamaz olarak
    // göstermeli.
    public decimal? TrendPercentage { get; set; }
}
