using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

public class InvalidReservationCodeException : NotFoundException
{
    public InvalidReservationCodeException() : base("InvalidReservationCode") { }
}
