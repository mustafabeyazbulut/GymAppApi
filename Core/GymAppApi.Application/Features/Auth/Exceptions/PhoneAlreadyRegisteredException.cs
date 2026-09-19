using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class PhoneAlreadyRegisteredException : ConflictException
{
    public PhoneAlreadyRegisteredException() : base("PhoneAlreadyRegistered") { }
}
