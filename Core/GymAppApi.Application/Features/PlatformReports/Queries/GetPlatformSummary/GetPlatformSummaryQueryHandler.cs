using GymAppApi.Application.Common.Time;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformSummary;

// Sistem Sahibi platform özeti. Tanımlar:
// - activeMemberCount: PackageAssignmentValidity'ye göre geçerli paketi olan tekil kullanıcı.
// - staffCount: aktif GymAdmin + BranchManager atamaları; trainerCount: aktif Trainer atamaları.
// - packageSalesInPeriod: dönemde oluşan (onaylanan) paket atamaları.
// - revenueInPeriod: dönemdeki ödemelerin toplamı.
// - userGrowth: dönemin her günü için yeni kayıt (boş günler 0), Türkiye yerel günü.
public class GetPlatformSummaryQueryHandler : IRequestHandler<GetPlatformSummaryQuery, PlatformSummaryDto>
{
    private readonly IPlatformReportReader _reader;

    public GetPlatformSummaryQueryHandler(IPlatformReportReader reader) => _reader = reader;

    public async Task<PlatformSummaryDto> Handle(GetPlatformSummaryQuery request, CancellationToken cancellationToken)
    {
        var period = ReportPeriod.LastDays(request.Days, DateTime.UtcNow);
        var data = await _reader.ReadAsync(period.FromUtc, period.NowUtc, companyId: null, cancellationToken);

        var newUsersByDate = data.NewUserCreatedAts
            .GroupBy(TurkeyCalendar.LocalDate)
            .ToDictionary(g => g.Key, g => g.Count());
        var userGrowth = Enumerable.Range(0, request.Days)
            .Select(offset => period.FromDate.AddDays(offset))
            .Select(date => new UserGrowthPointDto { Date = date, NewUsers = newUsersByDate.GetValueOrDefault(date) })
            .ToList();

        var branchesByCompany = data.Branches.ToLookup(b => b.CompanyId);
        var assignmentsByCompany = data.AssignmentCounts.ToLookup(a => a.CompanyId);
        var membersByCompany = data.ActiveMembers.ToLookup(m => m.CompanyId);
        var salesByCompany = data.Sales.ToLookup(s => s.CompanyId);
        var revenueByCompany = data.Revenue.ToLookup(r => r.CompanyId);

        var companies = data.Companies
            .OrderBy(c => c.Name)
            .Select(c => new PlatformCompanyReportDto
            {
                CompanyId = c.Id,
                CompanyName = c.Name,
                IsActive = c.IsActive,
                BranchCount = branchesByCompany[c.Id].Count(b => b.IsActive),
                ActiveMemberCount = membersByCompany[c.Id].Select(m => m.MemberUserId).Distinct().Count(),
                TrainerCount = TrainerCount(assignmentsByCompany[c.Id]),
                StaffCount = StaffCount(assignmentsByCompany[c.Id]),
                PackageSalesInPeriod = salesByCompany[c.Id].Sum(s => s.Count),
                RevenueInPeriod = revenueByCompany[c.Id].Sum(r => r.Amount),
            })
            .ToList();

        return new PlatformSummaryDto
        {
            TotalUsers = data.TotalUsers,
            NewUsersInPeriod = data.NewUserCreatedAts.Count,
            UserGrowth = userGrowth,
            CompanyCount = data.Companies.Count,
            ActiveCompanyCount = data.Companies.Count(c => c.IsActive),
            BranchCount = data.Branches.Count(b => b.IsActive),
            // Aynı kişi birden fazla firmada üye olabilir - platformda tek sayılır.
            ActiveMemberCount = data.ActiveMembers.Select(m => m.MemberUserId).Distinct().Count(),
            TrainerCount = TrainerCount(data.AssignmentCounts),
            StaffCount = StaffCount(data.AssignmentCounts),
            PackageSalesInPeriod = data.Sales.Sum(s => s.Count),
            RevenueInPeriod = data.Revenue.Sum(r => r.Amount),
            Companies = companies,
        };
    }

    internal static int TrainerCount(IEnumerable<AssignmentCountRow> rows) =>
        rows.Where(r => r.Role == AssignmentRole.Trainer).Sum(r => r.Count);

    internal static int StaffCount(IEnumerable<AssignmentCountRow> rows) =>
        rows.Where(r => r.Role is AssignmentRole.GymAdmin or AssignmentRole.BranchManager).Sum(r => r.Count);
}
