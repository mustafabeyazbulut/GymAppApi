using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Queries.GetPersonalLogs;

public class GetPersonalLogsQueryHandler : IRequestHandler<GetPersonalLogsQuery, IReadOnlyList<PersonalLogDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPersonalLogsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<PersonalLogDto>> Handle(GetPersonalLogsQuery request, CancellationToken cancellationToken)
    {
        // Geçersiz aralık bir sorgu parametresi hatası - 400 (gövde doğrulamasının 422'si değil).
        var (from, to) = request.ResolveRange();
        if (from > to)
        {
            throw new BadRequestException("PersonalLogRangeInvalid");
        }
        if (to.DayNumber - from.DayNumber > GetPersonalLogsQuery.MaxRangeDays)
        {
            throw new BadRequestException("PersonalLogRangeTooLong", GetPersonalLogsQuery.MaxRangeDays);
        }

        var logs = await _unitOfWork.GetReadRepository<PersonalLog>().GetAllAsync(
            l => l.UserId == request.UserId && l.Date >= from && l.Date <= to,
            orderBy: q => q.OrderByDescending(l => l.Date).ThenByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id),
            cancellationToken: cancellationToken);
        return logs.Select(PersonalLogDto.From).ToList();
    }
}
