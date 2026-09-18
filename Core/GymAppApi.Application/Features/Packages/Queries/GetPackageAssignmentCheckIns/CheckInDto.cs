namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentCheckIns;

public class CheckInDto
{
    public int Id { get; set; }
    public int? ReservationId { get; set; }
    public DateTime CheckedInAt { get; set; }
}
