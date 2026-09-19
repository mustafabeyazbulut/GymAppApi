using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

// Thrown whenever an action (cancel, no-show, check-in) requires a
// Reservation to still be in the Booked state - it's already CheckedIn,
// Cancelled or NoShow.
public class ReservationNotBookedException : ConflictException
{
    public ReservationNotBookedException() : base("ReservationNotBooked") { }
}
