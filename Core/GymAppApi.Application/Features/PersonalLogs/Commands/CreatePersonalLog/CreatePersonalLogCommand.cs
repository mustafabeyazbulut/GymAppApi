using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Commands.CreatePersonalLog;

public class CreatePersonalLogCommand : PersonalLogFields, IRequest<PersonalLogDto>
{
    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int UserId { get; set; }
}
