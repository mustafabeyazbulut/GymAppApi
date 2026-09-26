using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Commands.UpdatePersonalLog;

// PUT: tam değiştirme - gövdede verilmeyen alanlar null olur.
public class UpdatePersonalLogCommand : PersonalLogFields, IRequest<PersonalLogDto>
{
    // Controller tarafından route'tan set edilir.
    public int Id { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int UserId { get; set; }
}
