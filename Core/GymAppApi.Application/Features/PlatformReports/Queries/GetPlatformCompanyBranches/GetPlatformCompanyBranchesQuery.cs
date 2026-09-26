using MediatR;

namespace GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformCompanyBranches;

public class GetPlatformCompanyBranchesQuery : IRequest<IReadOnlyList<PlatformBranchReportDto>>
{
    public int CompanyId { get; set; }

    // 7 | 30 | 90 | 365 (bkz. ReportPeriod).
    public int Days { get; set; }
}
