using MediatR;

namespace GymAppApi.Application.Features.Reports.Queries.GetRevenueReport;

public class GetRevenueReportQuery : IRequest<RevenueReportDto>
{
    // null = son 30 gün (varsayılan) - GetAnalyticsSummaryQuery'nin sabit 30
    // günlük penceresinden farklı olarak burada GymAdmin kendi aralığını
    // seçebilsin diye opsiyonel.
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}
