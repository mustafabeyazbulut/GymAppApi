using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.ClassScheduling.Exceptions;

// Thrown whenever cancel is called on a ClassEnrollment that's already
// Attended/Cancelled/NoShow - mirrors ReservationNotBookedException.
public class ClassEnrollmentNotReservedException : ConflictException
{
    public ClassEnrollmentNotReservedException() : base("ClassEnrollmentNotReserved") { }
}
