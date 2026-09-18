using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CheckInReservationByCode;

public class CheckInReservationByCodeCommand : IRequest
{
    public string Code { get; set; } = null!;
    public int RequestedByUserId { get; set; }
}
