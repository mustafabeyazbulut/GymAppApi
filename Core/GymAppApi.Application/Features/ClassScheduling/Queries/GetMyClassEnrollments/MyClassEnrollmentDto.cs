namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetMyClassEnrollments;

public class MyClassEnrollmentDto
{
    public int Id { get; set; }
    public int ClassSessionId { get; set; }
    public string ClassName { get; set; } = null!;
    public string Category { get; set; } = null!;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Status { get; set; } = null!;
}
