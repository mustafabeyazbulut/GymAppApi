using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class PackageAssignmentNotFrozenException : ConflictException
{
    public PackageAssignmentNotFrozenException() : base("PackageAssignmentNotFrozen") { }
}
