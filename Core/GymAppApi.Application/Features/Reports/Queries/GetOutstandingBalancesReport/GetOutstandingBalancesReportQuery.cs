using MediatR;

namespace GymAppApi.Application.Features.Reports.Queries.GetOutstandingBalancesReport;

public class GetOutstandingBalancesReportQuery : IRequest<IReadOnlyList<OutstandingBalanceDto>>
{
}
