using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class ConflictingAssignmentRoleException : ConflictException
{
    public ConflictingAssignmentRoleException() : base("Bu kullanıcı bu firmada zaten Gym Admin veya Branch Manager rolüne sahip; aynı firmada ikisi birden olamaz.") { }
}
