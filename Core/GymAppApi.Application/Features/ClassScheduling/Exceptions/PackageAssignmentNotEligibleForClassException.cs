using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.ClassScheduling.Exceptions;

public class PackageAssignmentNotEligibleForClassException : ConflictException
{
    public PackageAssignmentNotEligibleForClassException() : base("PackageAssignmentNotEligibleForClass") { }
}
