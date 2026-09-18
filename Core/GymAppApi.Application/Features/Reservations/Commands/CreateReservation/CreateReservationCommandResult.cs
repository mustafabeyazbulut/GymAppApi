namespace GymAppApi.Application.Features.Reservations.Commands.CreateReservation;

public class CreateReservationCommandResult
{
    public int Id { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string QrCode { get; set; } = null!;
}
