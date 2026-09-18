using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CancelReservation;

public class CancelReservationCommand : IRequest
{
    // Set by the controller from the route segment.
    public int ReservationId { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
