using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CheckInReservation;

public class CheckInReservationCommand : IRequest
{
    public int ReservationId { get; set; }
    public int RequestedByUserId { get; set; }
}
