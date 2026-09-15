using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class AssignmentUserNotFoundException : NotFoundException
{
    public AssignmentUserNotFoundException(int userId) : base($"Kullanıcı {userId} bulunamadı.") { }
}
