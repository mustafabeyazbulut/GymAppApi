using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Reservations.Exceptions;

public class NoRemainingSessionsException : ConflictException
{
    public NoRemainingSessionsException() : base("Bu paket atamasının kalan seans hakkı yok.") { }
}
