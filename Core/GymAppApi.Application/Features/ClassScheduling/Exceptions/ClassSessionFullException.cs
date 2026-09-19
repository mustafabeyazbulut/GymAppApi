using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.ClassScheduling.Exceptions;

public class ClassSessionFullException : ConflictException
{
    public ClassSessionFullException() : base("ClassSessionFull") { }
}
