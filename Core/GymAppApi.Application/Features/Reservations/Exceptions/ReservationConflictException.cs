using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

public class ReservationConflictException : ConflictException
{
    public ReservationConflictException() : base("Bu antrenörün bu saatte zaten bir rezervasyonu var.") { }
}
