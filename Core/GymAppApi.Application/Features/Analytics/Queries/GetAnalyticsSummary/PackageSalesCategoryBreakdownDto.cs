namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public class PackageSalesCategoryBreakdownDto
{
    // null = Package.Category set edilmemiş (ör. sadece 1:1 Reservation için
    // kullanılan bir PT paketi - hiçbir grup dersi kategorisine ait değil).
    public string? Category { get; set; }
    public int Count { get; set; }
}
