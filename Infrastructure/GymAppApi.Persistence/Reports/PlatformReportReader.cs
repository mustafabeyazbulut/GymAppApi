using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Application.Features.PlatformReports;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Reports;

// Her kalem tek bir toplu sorgu: sayımlar veritabanında GroupBy ile, üye ve
// yeni kayıt listeleri sadece gereken kolonların projeksiyonuyla okunur.
// Firma/şube sayısından bağımsız sabit sayıda sorgu (N+1 yok). IReadRepository
// sadece varlık listesi döndürdüğü için doğrudan DbContext kullanılıyor
// (TenantResolutionService'in aynı gerekçesi). AsNoTracking: salt okuma.
public class PlatformReportReader : IPlatformReportReader
{
    private readonly GymAppApiDbContext _dbContext;

    public PlatformReportReader(GymAppApiDbContext dbContext) => _dbContext = dbContext;

    public async Task<PlatformReportData> ReadAsync(DateTime fromUtc, DateTime nowUtc, int? companyId, CancellationToken cancellationToken)
    {
        var users = _dbContext.Users.AsNoTracking().IgnoreQueryFilters();
        var totalUsers = await users.CountAsync(cancellationToken);
        var newUserCreatedAts = await users
            .Where(u => u.CreatedAt >= fromUtc && u.CreatedAt <= nowUtc)
            .Select(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        var companies = await _dbContext.Companies.AsNoTracking().IgnoreQueryFilters()
            .Where(c => companyId == null || c.Id == companyId)
            .Select(c => new CompanyRow(c.Id, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);

        var branches = await _dbContext.Branches.AsNoTracking().IgnoreQueryFilters()
            .Where(b => companyId == null || b.CompanyId == companyId)
            .Select(b => new BranchRow(b.Id, b.CompanyId, b.Name, b.IsActive))
            .ToListAsync(cancellationToken);

        var assignmentCounts = (await _dbContext.Assignments.AsNoTracking().IgnoreQueryFilters()
                .Where(a => a.IsActive && a.CompanyId != null && a.Role != AssignmentRole.SuperAdmin &&
                            (companyId == null || a.CompanyId == companyId))
                .GroupBy(a => new { a.CompanyId, a.BranchId, a.Role })
                .Select(g => new { g.Key.CompanyId, g.Key.BranchId, g.Key.Role, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .Select(x => new AssignmentCountRow(x.CompanyId!.Value, x.BranchId, x.Role, x.Count))
            .ToList();

        var activeMembers = (await _dbContext.PackageAssignments.AsNoTracking().IgnoreQueryFilters()
                .Where(PackageAssignmentValidity.Usable(nowUtc))
                .Where(pa => companyId == null || pa.CompanyId == companyId)
                .Select(pa => new { pa.CompanyId, pa.BranchId, pa.MemberUserId })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(x => new ActiveMemberRow(x.CompanyId, x.BranchId, x.MemberUserId))
            .ToList();

        var sales = (await _dbContext.PackageAssignments.AsNoTracking().IgnoreQueryFilters()
                .Where(pa => pa.CreatedAt >= fromUtc && pa.CreatedAt <= nowUtc && (companyId == null || pa.CompanyId == companyId))
                .GroupBy(pa => new { pa.CompanyId, pa.BranchId })
                .Select(g => new { g.Key.CompanyId, g.Key.BranchId, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .Select(x => new ScopedCountRow(x.CompanyId, x.BranchId, x.Count))
            .ToList();

        // Ödemenin kendi şube alanı yok - şube, ödemenin paket atamasından.
        var revenue = (await _dbContext.PackageAssignmentPayments.AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.PaidAt >= fromUtc && p.PaidAt <= nowUtc && (companyId == null || p.CompanyId == companyId))
                .GroupBy(p => new { p.CompanyId, p.PackageAssignment!.BranchId })
                .Select(g => new { g.Key.CompanyId, g.Key.BranchId, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken))
            .Select(x => new ScopedAmountRow(x.CompanyId, x.BranchId, x.Amount))
            .ToList();

        return new PlatformReportData(totalUsers, newUserCreatedAts, companies, branches, assignmentCounts, activeMembers, sales, revenue);
    }
}
