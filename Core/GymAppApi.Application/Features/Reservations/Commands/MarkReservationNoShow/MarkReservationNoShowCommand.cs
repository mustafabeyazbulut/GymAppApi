using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.MarkReservationNoShow;

public class MarkReservationNoShowCommand : IRequest
{
    public int ReservationId { get; set; }
    public int RequestedByUserId { get; set; }
}
