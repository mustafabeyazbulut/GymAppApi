using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

public class PackageAssignmentNotEligibleForReservationException : ConflictException
{
    public PackageAssignmentNotEligibleForReservationException() : base("PackageAssignmentNotEligibleForReservation") { }
}
