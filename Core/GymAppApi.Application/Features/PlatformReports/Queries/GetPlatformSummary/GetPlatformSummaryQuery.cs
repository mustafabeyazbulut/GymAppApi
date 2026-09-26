using MediatR;

namespace GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformSummary;

public class GetPlatformSummaryQuery : IRequest<PlatformSummaryDto>
{
    // 7 | 30 | 90 | 365 (bkz. ReportPeriod).
    public int Days { get; set; }
}
