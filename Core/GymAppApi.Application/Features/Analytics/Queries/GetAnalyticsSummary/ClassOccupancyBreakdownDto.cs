namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

// ClassSession.Name'e göre gruplanmış - "ders" tek bir oturum değil, aynı
// isimdeki (ör. "Yoga Başlangıç") tüm oturumların son 30 günlük toplamı.
public class ClassOccupancyBreakdownDto
{
    public string ClassName { get; set; } = null!;
    public int SessionCount { get; set; }
    public int TotalCapacity { get; set; }
    public int TotalEnrolled { get; set; }
    public decimal OccupancyRate { get; set; }
}
