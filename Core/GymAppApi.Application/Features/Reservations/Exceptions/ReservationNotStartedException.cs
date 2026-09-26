using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

// Saati henüz gelmemiş randevu "gelmedi" (no-show) olarak işaretlenemez.
public class ReservationNotStartedException : ConflictException
{
    public ReservationNotStartedException() : base("ReservationNotStarted") { }
}
