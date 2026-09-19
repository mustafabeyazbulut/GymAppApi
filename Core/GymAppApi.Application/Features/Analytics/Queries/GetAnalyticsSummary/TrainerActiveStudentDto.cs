namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public class TrainerActiveStudentDto
{
    public int TrainerUserId { get; set; }
    public string TrainerFullName { get; set; } = null!;
    public int ActiveStudentCount { get; set; }
}
