using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

// Pasife alınmış paket yeni bir üyeye tanımlanamaz.
public class PackageInactiveException : ConflictException
{
    public PackageInactiveException() : base("PackageInactive") { }
}
