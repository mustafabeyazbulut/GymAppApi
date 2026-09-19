using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class EmailAlreadyRegisteredException : ConflictException
{
    public EmailAlreadyRegisteredException() : base("EmailAlreadyRegistered") { }
}
