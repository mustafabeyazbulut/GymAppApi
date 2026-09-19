using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class FreezeLimitExceededException : ConflictException
{
    public FreezeLimitExceededException(int maxFreezeDays) : base("FreezeLimitExceeded", maxFreezeDays) { }
}
