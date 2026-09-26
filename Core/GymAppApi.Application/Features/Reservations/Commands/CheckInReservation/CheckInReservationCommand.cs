using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.Reservations.Commands.CheckInReservation;

// ITransactionalRequest: rezervasyon ve paket ataması FOR UPDATE ile
// kilitlenir (aynı hak iki kez düşülmesin); kilit transaction içinde anlamlı.
public class CheckInReservationCommand : IRequest, ITransactionalRequest
{
    public int ReservationId { get; set; }
    public int RequestedByUserId { get; set; }
}
