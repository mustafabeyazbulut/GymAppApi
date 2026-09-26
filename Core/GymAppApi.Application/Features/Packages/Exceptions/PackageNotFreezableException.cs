using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

// Paket MaxFreezeDays = 0 ile "dondurulamaz" tanımlanmış.
public class PackageNotFreezableException : ConflictException
{
    public PackageNotFreezableException() : base("PackageNotFreezable") { }
}
