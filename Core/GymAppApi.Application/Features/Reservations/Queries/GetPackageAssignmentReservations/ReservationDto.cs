namespace GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentReservations;

public class ReservationDto
{
    public int Id { get; set; }
    public int TrainerId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = null!;
    public string QrCode { get; set; } = null!;
}
