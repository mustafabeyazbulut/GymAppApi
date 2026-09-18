namespace GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentTrainers;

public class TrainerDto
{
    // User.Id - Reservation.TrainerId references this directly (see
    // CreateReservationCommand), not an Assignment id.
    public int Id { get; set; }
    public string FullName { get; set; } = null!;
    public int? BranchId { get; set; }
}
