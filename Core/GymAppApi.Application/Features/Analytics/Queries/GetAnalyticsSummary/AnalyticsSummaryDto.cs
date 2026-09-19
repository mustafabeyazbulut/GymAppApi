namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public class AnalyticsSummaryDto
{
    public ActiveMemberMetricDto ActiveMembers { get; set; } = null!;
    public ClassOccupancyMetricDto ClassOccupancy { get; set; } = null!;
    public PackageSalesMetricDto PackageSales { get; set; } = null!;
    public IReadOnlyList<TrainerActiveStudentDto> TrainerActiveStudents { get; set; } = Array.Empty<TrainerActiveStudentDto>();
}
