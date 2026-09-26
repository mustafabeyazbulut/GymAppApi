using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CheckInReservationByCode;

// ITransactionalRequest: bkz. CheckInReservationCommand.
public class CheckInReservationByCodeCommand : IRequest, ITransactionalRequest
{
    public string Code { get; set; } = null!;
    public int RequestedByUserId { get; set; }
}
