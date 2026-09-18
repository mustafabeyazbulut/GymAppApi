using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class MemberAlreadyHasThisPackageException : ConflictException
{
    public MemberAlreadyHasThisPackageException() : base("Bu üyenin bu pakete zaten aktif bir ataması var.") { }
}
