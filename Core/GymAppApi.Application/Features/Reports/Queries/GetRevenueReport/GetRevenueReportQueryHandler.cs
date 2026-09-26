using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reports.Queries.GetRevenueReport;

// GetAnalyticsSummaryQueryHandler'ın aynı ambient ITenantContext deseni:
// Gym Admin kendi firmasının tüm şubeleri (Sistem Sahibi erişemez), Şube
// Yöneticisi sadece kendi şubesi. PackageAssignmentPayment'ın kendi
// BranchId alanı yok (sadece CompanyId) - şube daraltması bu yüzden
// PackageAssignment.BranchId üzerinden (Include ile) yapılıyor.
public class GetRevenueReportQueryHandler : IRequestHandler<GetRevenueReportQuery, RevenueReportDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetRevenueReportQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<RevenueReportDto> Handle(GetRevenueReportQuery request, CancellationToken cancellationToken)
    {
        var branchId = _tenantContext.BranchId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = request.FromDate ?? today.AddDays(-30);
        var toDate = request.ToDate ?? today;
        // Gün sonuna kadarki ödemeleri de kapsasın diye üst sınır bir gün
        // ileriye alınıp "<" ile karşılaştırılıyor (aralarında saat/dakika
        // taşıyan PaidAt değerlerini kaçırmamak için). DateOnly.ToDateTime,
        // Kind=Unspecified bir DateTime üretiyor - Npgsql "timestamp with
        // time zone" kolonlarına sadece Kind=Utc kabul ediyor (bkz. canlı
        // testte alınan "Cannot write DateTime with Kind=Unspecified"
        // hatası), bu yüzden burada açıkça UTC olarak işaretleniyor.
        var fromDateTime = DateTime.SpecifyKind(fromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toDateTimeExclusive = DateTime.SpecifyKind(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var payments = await _unitOfWork.GetReadRepository<PackageAssignmentPayment>().GetAllAsync(
            p => p.PaidAt >= fromDateTime && p.PaidAt < toDateTimeExclusive,
            include: q => q.Include(p => p.PackageAssignment),
            cancellationToken: cancellationToken);

        var scopedPayments = branchId == null
            ? payments
            : payments.Where(p => p.PackageAssignment!.BranchId == branchId).ToList();

        var methodBreakdown = scopedPayments
            .GroupBy(p => p.Method)
            .Select(g => new RevenueMethodBreakdownDto { Method = g.Key.ToString(), Amount = g.Sum(p => p.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var dailyBreakdown = scopedPayments
            .GroupBy(p => DateOnly.FromDateTime(p.PaidAt))
            .Select(g => new RevenueDailyBreakdownDto { Date = g.Key, Amount = g.Sum(p => p.Amount) })
            .OrderBy(x => x.Date)
            .ToList();

        return new RevenueReportDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            TotalAmount = scopedPayments.Sum(p => p.Amount),
            MethodBreakdown = methodBreakdown,
            DailyBreakdown = dailyBreakdown,
        };
    }
}
