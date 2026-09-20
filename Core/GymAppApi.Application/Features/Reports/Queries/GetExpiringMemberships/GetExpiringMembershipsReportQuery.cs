using MediatR;

namespace GymAppApi.Application.Features.Reports.Queries.GetExpiringMemberships;

public class GetExpiringMembershipsReportQuery : IRequest<IReadOnlyList<ExpiringMembershipDto>>
{
    // Varsayılan 30 gün - bugünden itibaren bu kadar gün içinde (veya zaten
    // geçmiş) bitecek üyelikleri döner, GymAdmin'in yenileme araması yapması
    // için.
    public int DaysAhead { get; set; } = 30;
}
