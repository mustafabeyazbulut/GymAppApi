using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CreateReservation;

public class CreateReservationCommand : IRequest<CreateReservationCommandResult>
{
    public int PackageAssignmentId { get; set; }
    public int TrainerId { get; set; }
    public DateTime ScheduledAt { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
