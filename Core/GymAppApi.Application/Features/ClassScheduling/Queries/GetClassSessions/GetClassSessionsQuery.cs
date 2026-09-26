using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;

public class GetClassSessionsQuery : IRequest<IReadOnlyList<ClassSessionDto>>
{
    public int? BranchId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    // Controller tarafından JWT'den doldurulur - üyenin kendi geçerli
    // paketlerinden görünürlük kapsamını çıkarmak için.
    public int RequestedByUserId { get; set; }
}
