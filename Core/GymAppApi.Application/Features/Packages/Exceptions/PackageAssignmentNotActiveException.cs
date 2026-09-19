using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class PackageAssignmentNotActiveException : ConflictException
{
    public PackageAssignmentNotActiveException() : base("PackageAssignmentNotActive") { }
}
