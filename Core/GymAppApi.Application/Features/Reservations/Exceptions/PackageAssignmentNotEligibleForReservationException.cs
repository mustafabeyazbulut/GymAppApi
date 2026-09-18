using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

public class PackageAssignmentNotEligibleForReservationException : ConflictException
{
    public PackageAssignmentNotEligibleForReservationException()
        : base("Bu paket ataması rezervasyon için uygun değil (aktif, seans bazlı ve seans hakkı kalmış olmalı).") { }
}
