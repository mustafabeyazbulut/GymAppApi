namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;

public class ClassSessionDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int TrainerUserId { get; set; }
    public string Category { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Capacity { get; set; }
    public int EnrolledCount { get; set; }
    public int CancellationCutoffHours { get; set; }
}
