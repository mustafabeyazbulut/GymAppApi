using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformSummary;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformCompanyBranches;

// Bir firmanın şube kırılımı - tanımlar GetPlatformSummaryQueryHandler ile
// aynı; şubeye bağlı olmayan kayıtlar (firma geneli Gym Admin, eski şubesiz
// paketler) şube satırlarına girmez. Şube düzeyinde personel = Şube Yöneticisi.
public class GetPlatformCompanyBranchesQueryHandler : IRequestHandler<GetPlatformCompanyBranchesQuery, IReadOnlyList<PlatformBranchReportDto>>
{
    private readonly IPlatformReportReader _reader;

    public GetPlatformCompanyBranchesQueryHandler(IPlatformReportReader reader) => _reader = reader;

    public async Task<IReadOnlyList<PlatformBranchReportDto>> Handle(GetPlatformCompanyBranchesQuery request, CancellationToken cancellationToken)
    {
        var period = ReportPeriod.LastDays(request.Days, DateTime.UtcNow);
        var data = await _reader.ReadAsync(period.FromUtc, period.NowUtc, request.CompanyId, cancellationToken);
        if (data.Companies.Count == 0)
        {
            throw new NotFoundException("CompanyNotFound", request.CompanyId);
        }

        var assignmentsByBranch = data.AssignmentCounts.Where(a => a.BranchId != null).ToLookup(a => a.BranchId!.Value);
        var membersByBranch = data.ActiveMembers.Where(m => m.BranchId != null).ToLookup(m => m.BranchId!.Value);
        var salesByBranch = data.Sales.Where(s => s.BranchId != null).ToLookup(s => s.BranchId!.Value);
        var revenueByBranch = data.Revenue.Where(r => r.BranchId != null).ToLookup(r => r.BranchId!.Value);

        return data.Branches
            .OrderBy(b => b.Name)
            .Select(b => new PlatformBranchReportDto
            {
                BranchId = b.Id,
                BranchName = b.Name,
                IsActive = b.IsActive,
                ActiveMemberCount = membersByBranch[b.Id].Select(m => m.MemberUserId).Distinct().Count(),
                TrainerCount = GetPlatformSummaryQueryHandler.TrainerCount(assignmentsByBranch[b.Id]),
                StaffCount = assignmentsByBranch[b.Id].Where(a => a.Role == AssignmentRole.BranchManager).Sum(a => a.Count),
                PackageSalesInPeriod = salesByBranch[b.Id].Sum(s => s.Count),
                RevenueInPeriod = revenueByBranch[b.Id].Sum(r => r.Amount),
            })
            .ToList();
    }
}
