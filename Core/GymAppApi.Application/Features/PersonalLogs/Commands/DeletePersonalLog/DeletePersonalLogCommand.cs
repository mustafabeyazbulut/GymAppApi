using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Commands.DeletePersonalLog;

public class DeletePersonalLogCommand : IRequest
{
    public int Id { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int UserId { get; set; }
}
