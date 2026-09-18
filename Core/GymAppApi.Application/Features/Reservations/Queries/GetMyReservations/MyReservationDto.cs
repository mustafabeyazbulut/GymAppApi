namespace GymAppApi.Application.Features.Reservations.Queries.GetMyReservations;

public class MyReservationDto
{
    public int Id { get; set; }
    public int PackageAssignmentId { get; set; }
    public int MemberUserId { get; set; }
    public string? MemberFullName { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = null!;
    public string QrCode { get; set; } = null!;
}
