using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Queries.GetPersonalLogs;

public class GetPersonalLogsQuery : IRequest<IReadOnlyList<PersonalLogDto>>
{
    // Mobil from=bugün-365, to=bugün ile çağırıyor; bir gün pay bırakıldı.
    public const int MaxRangeDays = 366;
    public const int DefaultRangeDays = 365;

    // Verilmezse: To = bugün, From = To - 365 gün.
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int UserId { get; set; }

    public (DateOnly From, DateOnly To) ResolveRange()
    {
        var to = To ?? From?.AddDays(DefaultRangeDays) ?? PersonalLogFields.LatestLocalToday();
        var from = From ?? to.AddDays(-DefaultRangeDays);
        return (from, to);
    }
}
